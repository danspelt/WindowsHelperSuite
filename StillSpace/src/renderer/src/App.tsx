import { useState } from 'react'
import { MainRoom } from './components/MainRoom'
import { WelcomeScreen } from './components/WelcomeScreen'
import { SettingsPanel } from './features/settings/SettingsPanel'
import { useSessionStore } from './stores/sessionStore'

export default function App() {
  const started = useSessionStore((s) => s.started)
  const endSession = useSessionStore((s) => s.endSession)
  const [settingsOpen, setSettingsOpen] = useState(false)

  return (
    <>
      {started ? (
        <MainRoom onOpenSettings={() => setSettingsOpen(true)} onEndSession={() => endSession()} />
      ) : (
        <WelcomeScreen onOpenSettings={() => setSettingsOpen(true)} />
      )}
      {settingsOpen ? <SettingsPanel onClose={() => setSettingsOpen(false)} /> : null}
    </>
  )
}
