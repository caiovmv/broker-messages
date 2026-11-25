import { expect, test } from "@playwright/test";

test.use({ viewport: { width: 1280, height: 800 } });

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem("broker.locale", "en"));
});

test("applications stay in memory until a save asks for them again", async ({ page }) => {
  await page.goto("/");
  await page.getByLabel("User").fill("operador");
  await page.getByLabel("Password").fill("operador-local");
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Applications" })).toBeVisible({ timeout: 30_000 });

  const reads: string[] = [];
  page.on("request", (request) => {
    const url = new URL(request.url());
    if (request.method() === "GET" && url.pathname === "/api/aplicativos") reads.push(url.pathname);
  });

  await page.getByRole("button", { name: "Health" }).click();
  await expect(page.getByRole("heading", { name: "Health" })).toBeVisible();
  await page.getByRole("button", { name: "Applications" }).click();
  await expect(page.getByRole("heading", { name: "Applications" })).toBeVisible();
  expect(reads).toEqual([]);
});
