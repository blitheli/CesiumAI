import css from "./styles.css?raw";

it("keeps the desktop viewer fluid beside a 380px chat panel", () => {
  expect(css).toMatch(
    /\.app-shell\s*\{[^}]*height:\s*100(?:svh|dvh|vh)[^}]*grid-template-columns:\s*minmax\(0,\s*1fr\)\s+380px/s,
  );
});

it("moves chat below the viewer at 800px with a 45% height cap", () => {
  expect(css).toMatch(/@media\s*\(max-width:\s*800px\)/);
  expect(css).toMatch(
    /@media\s*\(max-width:\s*800px\)[\s\S]*?\.app-shell\s*\{[^}]*grid-template-columns:\s*1fr[^}]*grid-template-rows:\s*minmax\(0,\s*1fr\)\s+minmax\(0,\s*45%\)/,
  );
  expect(css).toMatch(
    /@media\s*\(max-width:\s*800px\)[\s\S]*?\.chat-panel\s*\{[^}]*max-height:\s*45(?:svh|dvh|vh)/,
  );
});
