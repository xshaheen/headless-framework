import babel from '@rolldown/plugin-babel'
import react, { reactCompilerPreset } from '@vitejs/plugin-react'
import type { PluginOption } from 'vite'

/** React with the React Compiler, the same in the shell and every module. */
export function reactPlugins(): PluginOption[] {
  return [react(), babel({ presets: [reactCompilerPreset()] })]
}
