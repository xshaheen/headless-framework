import axe from 'axe-core'

/**
 * Runs axe-core's WCAG 2.2 A and AA rules over `root` (the whole page by default, so popovers and dialogs portalled to
 * the body count) and lists each violation as `rule: element`, so a failing test names what broke without a dump of
 * the markup. Contrast is left out: jsdom lays nothing out, so axe cannot compute a colour against its background.
 */
export async function axeViolations(root: Element = document.body): Promise<string[]> {
  const result = await axe.run(root, {
    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa', 'best-practice'] },
    rules: {
      'color-contrast': { enabled: false },
      // A module renders without the shell's landmarks in a test, so its content sits outside any of them.
      region: { enabled: false },
    },
    resultTypes: ['violations'],
  })

  return result.violations.flatMap((violation) =>
    violation.nodes.map((node) => `${violation.id}: ${node.target.join(' ')}`),
  )
}
