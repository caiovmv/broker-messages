import { expect, test } from "@playwright/test";

const user = process.env.BROKER_OPERADOR ?? "operador";
const password = process.env.BROKER_OPERADOR_SENHA ?? "operador-local";

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem("broker.locale", "en"));
});

test("a signed-out visitor sees the operator sign-in", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "Operator sign-in" })).toBeVisible();
  await expect(page.getByLabel("User")).toBeVisible();
});

test("the operator creates an application and sees the credentials", async ({ page }) => {
  await page.goto("/");
  await page.getByLabel("User").fill(user);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { name: "Applications" })).toBeVisible({ timeout: 30_000 });

  const name = "App " + Date.now().toString().slice(-6);
  await page.getByLabel("Name").fill(name);
  await page.getByRole("button", { name: "Create application" }).click();
  await expect(page.getByText(name).first()).toBeVisible();
  await expect(page.getByText("API token")).toBeVisible();
  await expect(page.getByText("SMTP user")).toBeVisible();
});
