export type Tema = "light" | "dark";

export function lerTema(): Tema {
  const stored = localStorage.getItem("broker.theme");
  if (stored === "dark" || stored === "light") {
    return stored;
  }

  return "dark";
}

export function pintarTema(tema: Tema) {
  document.documentElement.classList.toggle("light", tema === "light");
  localStorage.setItem("broker.theme", tema);
}
