CREATE TABLE IF NOT EXISTS chat_conversations (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
    title text NOT NULL DEFAULT 'Yeni konuşma',
    context jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_chat_conversations_user_updated
    ON chat_conversations (user_id, updated_at DESC);

CREATE TABLE IF NOT EXISTS chat_messages (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    conversation_id uuid NOT NULL REFERENCES chat_conversations(id) ON DELETE CASCADE,
    role text NOT NULL CHECK (role IN ('user', 'assistant')),
    content text NOT NULL CHECK (char_length(content) BETWEEN 1 AND 1000),
    client_message_id uuid,
    reply_to_message_id uuid REFERENCES chat_messages(id) ON DELETE CASCADE,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (conversation_id, client_message_id),
    UNIQUE (reply_to_message_id)
);

CREATE INDEX IF NOT EXISTS ix_chat_messages_conversation_created
    ON chat_messages (conversation_id, created_at, id);
