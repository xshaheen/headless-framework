import { Button, useObject } from '@headless/dashboard-sdk'
import { useState } from 'react'

export default function Home() {
  const session = useObject<{ authenticated: boolean; name?: string }>('session', 'current')
  const [presses, setPresses] = useState(0)

  return (
    <section aria-labelledby="diagnostics-title">
      <h1 id="diagnostics-title">Diagnostics</h1>
      <p data-testid="session">{session ? `Signed in as ${session.name ?? 'anonymous'}` : 'No session'}</p>
      <Button onPress={() => setPresses((count) => count + 1)}>Pressed {presses}</Button>
    </section>
  )
}
