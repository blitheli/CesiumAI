import { render, screen } from "@testing-library/react";
import { App } from "./App";

it("renders the CesiumAI shell", () => {
  render(<App />);
  expect(screen.getByRole("main", { name: "CesiumAI" })).toBeInTheDocument();
});
