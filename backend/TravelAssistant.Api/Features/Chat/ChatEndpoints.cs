using Npgsql;
using System.Text.Json;
using TravelAssistant.Api.Features.Auth;

namespace TravelAssistant.Api.Features.Chat;

internal static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/chat/conversations", GetConversationsAsync);
        app.MapPost("/api/chat/conversations", CreateConversationAsync);
        app.MapGet("/api/chat/conversations/{conversationId:guid}/messages", GetMessagesAsync);
        app.MapPost("/api/chat/conversations/{conversationId:guid}/messages", SendMessageAsync);
        return app;
    }

    private static async Task<IResult> GetConversationsAsync(HttpRequest request, IConfiguration configuration, SessionStore sessions, CancellationToken cancellationToken)
    {
        if (!sessions.TryGet(request, out var session)) return Results.Unauthorized();
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT id, title, created_at, updated_at FROM chat_conversations WHERE user_id=@user_id ORDER BY updated_at DESC", connection); command.Parameters.AddWithValue("user_id", session.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); var conversations = new List<object>();
        while (await reader.ReadAsync(cancellationToken)) conversations.Add(new { id = reader.GetGuid(0), title = reader.GetString(1), createdAt = reader.GetDateTime(2), updatedAt = reader.GetDateTime(3) });
        return Results.Ok(conversations);
    }

    private static async Task<IResult> CreateConversationAsync(HttpRequest request, IConfiguration configuration, SessionStore sessions, CancellationToken cancellationToken)
    {
        if (!sessions.TryGet(request, out var session)) return Results.Unauthorized();
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("INSERT INTO chat_conversations (user_id) VALUES (@user_id) RETURNING id, title, created_at, updated_at", connection); command.Parameters.AddWithValue("user_id", session.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); await reader.ReadAsync(cancellationToken);
        return Results.Ok(new { id = reader.GetGuid(0), title = reader.GetString(1), createdAt = reader.GetDateTime(2), updatedAt = reader.GetDateTime(3) });
    }

    private static async Task<IResult> GetMessagesAsync(Guid conversationId, HttpRequest request, IConfiguration configuration, SessionStore sessions, CancellationToken cancellationToken)
    {
        if (!sessions.TryGet(request, out var session)) return Results.Unauthorized();
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT m.id, m.role, m.content, m.metadata::text, m.created_at
            FROM chat_messages m
            JOIN chat_conversations c ON c.id=m.conversation_id
            WHERE m.conversation_id=@conversation_id AND c.user_id=@user_id
            ORDER BY m.created_at, m.id
            """, connection);
        command.Parameters.AddWithValue("conversation_id", conversationId); command.Parameters.AddWithValue("user_id", session.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); var messages = new List<object>();
        while (await reader.ReadAsync(cancellationToken)) messages.Add(new { id = reader.GetGuid(0), role = reader.GetString(1), content = reader.GetString(2), metadata = JsonSerializer.Deserialize<JsonElement>(reader.GetString(3)), createdAt = reader.GetDateTime(4) });
        return Results.Ok(messages);
    }

    private static async Task<IResult> SendMessageAsync(Guid conversationId, ChatMessageRequest chatRequest, HttpRequest request, IConfiguration configuration, SessionStore sessions, AiTravelUnderstandingService aiUnderstanding, CancellationToken cancellationToken)
    {
        if (!sessions.TryGet(request, out var session)) return Results.Unauthorized();
        var content = chatRequest.Content?.Trim() ?? "";
        if (content.Length == 0) return Results.BadRequest(new { message = "Boş mesaj gönderilemez." });
        if (content.Length > 1000) return Results.BadRequest(new { message = "Mesaj en fazla 1000 karakter olabilir." });

        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken);
        await using (var ownership = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM chat_conversations WHERE id=@id AND user_id=@user_id)", connection))
        {
            ownership.Parameters.AddWithValue("id", conversationId); ownership.Parameters.AddWithValue("user_id", session.Id);
            if (!(bool)(await ownership.ExecuteScalarAsync(cancellationToken))!) return Results.NotFound(new { message = "Konuşma bulunamadı." });
        }

        Guid userMessageId;
        await using (var insertUser = new NpgsqlCommand("INSERT INTO chat_messages (conversation_id, role, content, client_message_id) VALUES (@conversation_id, 'user', @content, @client_id) ON CONFLICT (conversation_id, client_message_id) DO NOTHING RETURNING id", connection))
        {
            insertUser.Parameters.AddWithValue("conversation_id", conversationId); insertUser.Parameters.AddWithValue("content", content); insertUser.Parameters.AddWithValue("client_id", chatRequest.ClientMessageId);
            var inserted = await insertUser.ExecuteScalarAsync(cancellationToken);
            if (inserted is null)
            {
                await using var duplicate = new NpgsqlCommand("SELECT id FROM chat_messages WHERE conversation_id=@conversation_id AND client_message_id=@client_id", connection); duplicate.Parameters.AddWithValue("conversation_id", conversationId); duplicate.Parameters.AddWithValue("client_id", chatRequest.ClientMessageId);
                userMessageId = (Guid)(await duplicate.ExecuteScalarAsync(cancellationToken))!;
                await using var existingReply = new NpgsqlCommand("SELECT id, content, metadata::text, created_at FROM chat_messages WHERE reply_to_message_id=@reply_to", connection); existingReply.Parameters.AddWithValue("reply_to", userMessageId);
                await using var existingReader = await existingReply.ExecuteReaderAsync(cancellationToken);
                if (await existingReader.ReadAsync(cancellationToken)) return Results.Ok(new { userMessageId, assistant = new { id = existingReader.GetGuid(0), role = "assistant", content = existingReader.GetString(1), metadata = JsonSerializer.Deserialize<JsonElement>(existingReader.GetString(2)), createdAt = existingReader.GetDateTime(3) } });
            }
            else userMessageId = (Guid)inserted;
        }

        var aiOutcome = await aiUnderstanding.TryUnderstandAsync(content, cancellationToken);
        var reply = await TravelChatService.ReplyAsync(connection, conversationId, content, aiOutcome, cancellationToken);
        Guid assistantId; DateTime assistantCreatedAt;
        await using (var insertAssistant = new NpgsqlCommand("INSERT INTO chat_messages (conversation_id, role, content, reply_to_message_id, metadata) VALUES (@conversation_id, 'assistant', @content, @reply_to, @metadata::jsonb) ON CONFLICT (reply_to_message_id) DO UPDATE SET content=EXCLUDED.content RETURNING id, created_at", connection))
        {
            insertAssistant.Parameters.AddWithValue("conversation_id", conversationId); insertAssistant.Parameters.AddWithValue("content", reply.Content); insertAssistant.Parameters.AddWithValue("reply_to", userMessageId); insertAssistant.Parameters.AddWithValue("metadata", reply.MetadataJson);
            await using var reader = await insertAssistant.ExecuteReaderAsync(cancellationToken); await reader.ReadAsync(cancellationToken); assistantId = reader.GetGuid(0); assistantCreatedAt = reader.GetDateTime(1);
        }
        await using (var updateTitle = new NpgsqlCommand("UPDATE chat_conversations SET title=CASE WHEN title='Yeni konuşma' THEN LEFT(@title, 60) ELSE title END, updated_at=now() WHERE id=@id", connection))
        {
            updateTitle.Parameters.AddWithValue("title", content); updateTitle.Parameters.AddWithValue("id", conversationId); await updateTitle.ExecuteNonQueryAsync(cancellationToken);
        }
        return Results.Ok(new { userMessageId, assistant = new { id = assistantId, role = "assistant", content = reply.Content, metadata = JsonSerializer.Deserialize<JsonElement>(reply.MetadataJson), createdAt = assistantCreatedAt } });
    }
}
