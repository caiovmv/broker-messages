import type { ReactNode } from "react";
import { useI18n } from "./i18n";
import { Rail } from "./ui";
import { lerTema, pintarTema, type Tema } from "./theme";
import { useState } from "react";

const items = ["apps", "destinations", "domains", "deliveries", "health"] as const;

export function Shell({
  name,
  screen,
  onScreen,
  onSignOut,
  children,
}: {
  name: string;
  screen: string;
  onScreen: (id: string) => void;
  onSignOut: () => void;
  children: ReactNode;
}) {
  const { copy, locale, setLocale } = useI18n();
  const [theme, setTheme] = useState<Tema>(() => lerTema());
  const [open, setOpen] = useState(false);
  const nav = items.map((id) => ({ id, group: "operate", label: copy.nav[id] }));

  return (
    <div className="min-h-screen md:grid md:grid-cols-[248px_1fr]">
      <div className="md:hidden p-4">
        <button className="text-sm" type="button" onClick={() => setOpen(true)}>{copy.actions.menu}</button>
      </div>
      <div className={open ? "fixed inset-0 z-20 bg-black/40 md:static md:bg-transparent" : "hidden md:block"} onClick={() => setOpen(false)}>
        <div onClick={(event) => event.stopPropagation()}>
          <Rail
            brand={copy.brand}
            groups={[{ id: "operate", label: copy.groups.operate }]}
            items={nav}
            active={screen}
            onChoose={(id) => {
              onScreen(id);
              setOpen(false);
            }}
            footer={
              <div className="px-3 text-sm">
                <p className="mb-2">{name}</p>
                <button className="mr-3 underline" type="button" onClick={() => setLocale(locale === "pt-BR" ? "en" : "pt-BR")}>{copy.language}</button>
                <button
                  className="mr-3 underline"
                  type="button"
                  onClick={() => {
                    const next = theme === "dark" ? "light" : "dark";
                    setTheme(next);
                    pintarTema(next);
                  }}
                >
                  {theme === "dark" ? copy.actions.themeLight : copy.actions.themeDark}
                </button>
                <button className="underline" type="button" onClick={onSignOut}>{copy.actions.signOut}</button>
              </div>
            }
          />
        </div>
      </div>
      <main className="p-4 md:p-8">{children}</main>
    </div>
  );
}
