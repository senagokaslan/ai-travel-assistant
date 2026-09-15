import { BrowserRouter, Route, Routes } from 'react-router-dom'
import { AdminRoute, BookingsPage, LoginPage, ProfilePage } from './features/account/AccountPages'
import { AuthProvider } from './features/auth/AuthContext'
import { BookingSummaryPage } from './features/bookings/BookingSummaryPage'
import { ChatPage } from './features/chat/ChatPage'
import { FlightSearchPage } from './features/flights/FlightSearchPage'
import { HomePage } from './features/home/HomePage'
import { HotelDetailPage, HotelResultsPage, HotelSearchPage } from './features/hotels/HotelPages'
import { AppShell, ScrollToTop } from './layout/AppShell'
import { NotFoundPage, ProtectedRoute } from './shared/components/PageRoutes'
import './App.css'

function App() {
  return <AuthProvider><BrowserRouter><ScrollToTop /><Routes>
    <Route element={<AppShell />}>
      <Route index element={<HomePage />} /><Route path="hotels" element={<HotelSearchPage />} /><Route path="hotels/results" element={<HotelResultsPage />} /><Route path="hotels/:id" element={<HotelDetailPage />} />
      <Route path="flights" element={<FlightSearchPage />} /><Route path="booking/summary" element={<BookingSummaryPage />} /><Route path="chat" element={<ProtectedRoute><ChatPage /></ProtectedRoute>} /><Route path="bookings" element={<ProtectedRoute><BookingsPage /></ProtectedRoute>} /><Route path="profile" element={<ProtectedRoute><ProfilePage /></ProtectedRoute>} /><Route path="admin" element={<AdminRoute />} /><Route path="*" element={<NotFoundPage />} />
    </Route><Route path="/login" element={<LoginPage />} />
  </Routes></BrowserRouter></AuthProvider>
}

export default App
