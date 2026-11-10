// Draws an icon and a chart through the SDK's lazy entries, so its build must leave lucide and Recharts to the shell.
import { Chart } from '@headless/dashboard-sdk/charts'
import { RunsIcon } from '@headless/dashboard-sdk/icons'

export default function Home() {
  return (
    <>
      <RunsIcon />
      <Chart kind="line" label="Runs" unit="runs" zone="UTC" start={0} intervalSeconds={60} asOf={0} series={[]} />
    </>
  )
}
