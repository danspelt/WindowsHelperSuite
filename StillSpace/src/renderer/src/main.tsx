import React from 'react'
import ReactDOM from 'react-dom/client'
import App from './App'
import './index.css'

// Avoid React 18 StrictMode in Electron dev: double mount tears down the Vite/HMR client and can spam Chromium
// "chunked_data_pipe_upload_data_stream ... OnSizeReceived failed" while the app still works.
const root = document.getElementById('root')!
ReactDOM.createRoot(root).render(
  import.meta.env.PROD ? (
    <React.StrictMode>
      <App />
    </React.StrictMode>
  ) : (
    <App />
  )
)
