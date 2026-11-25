import { useEffect, useState, type FormEvent } from "react";
import { useLocation, useNavigate } from "react-router";
import { load, peek, post, remove, save } from "./api";
import { clearCache } from "./cache";
import { useI18n } from "./i18n";
import { Shell } from "./shell";
import { Field, Page, Skeleton, ToastProvider, control, useToast } from "./ui";
import type { Aplicativo, Destino, Dominio, Envio, Sessao } from "./types";

const screens = ["apps", "destinations", "domains", "deliveries", "health"];

function screenFrom(path: string): string {
  const name = path.replace(/^\/+/, "").split("/")[0];
  return screens.includes(name) ? name : "apps";
}

export function App() {
  return (
    <ToastProvider>
      <Gate />
    </ToastProvider>
  );
}

function Gate() {
  const tell = useToast();
  const { copy, feedback } = useI18n();
  const location = useLocation();
  const navigate = useNavigate();
  const [ready, setReady] = useState(false);
  const [session, setSession] = useState<Sessao | null>(null);
  const [screen, setScreen] = useState(screenFrom(location.pathname));
  const [appId, setAppId] = useState("");

  useEffect(() => {
    setScreen(screenFrom(location.pathname));
  }, [location.pathname]);

  useEffect(() => {
    load<Sessao>("/api/sessao", "session")
      .then((next) => {
        setSession(next);
        setReady(true);
      })
      .catch(() => {
        setReady(true);
      });
  }, []);

  if (!ready) {
    return <p className="p-8 text-sm">{copy.loading}</p>;
  }

  if (!session) {
    return <Login onDone={(next) => setSession(next)} />;
  }

  function choose(id: string) {
    setScreen(id);
    navigate("/" + id);
  }

  async function signOut() {
    await remove("/api/sessao", "session");
    clearCache();
    setSession(null);
  }

  return (
    <Shell name={session.nome} screen={screen} onScreen={choose} onSignOut={() => { signOut().catch((reason) => tell(feedback(reason), "err")); }}>
      {screen === "apps" && <Apps onPick={setAppId} selected={appId} />}
      {screen === "destinations" && <Destinations appId={appId} />}
      {screen === "domains" && <Domains appId={appId} />}
      {screen === "deliveries" && <Deliveries appId={appId} />}
      {screen === "health" && <Health />}
    </Shell>
  );
}

function Login({ onDone }: { onDone: (session: Sessao) => void }) {
  const { copy, feedback } = useI18n();
  const tell = useToast();
  const [usuario, setUsuario] = useState("");
  const [senha, setSenha] = useState("");

  async function submit(event: FormEvent) {
    event.preventDefault();
    try {
      const session = await post<Sessao>("/api/sessao", "session", { usuario, senha });
      onDone(session);
    } catch (reason) {
      tell(feedback(reason), "err");
    }
  }

  return (
    <main className="mx-auto max-w-md p-8">
      <Page title={copy.signInTitle} lede={copy.signInLede}>
        <form className="cartao grid gap-4 p-4" onSubmit={submit}>
          <Field label={copy.user}>
            <input className={control} value={usuario} onChange={(event) => setUsuario(event.target.value)} />
          </Field>
          <Field label={copy.password}>
            <input className={control} type="password" value={senha} onChange={(event) => setSenha(event.target.value)} />
          </Field>
          <button className="acao rounded-2xl px-4 py-2 text-sm" type="submit">{copy.enter}</button>
        </form>
      </Page>
    </main>
  );
}

function Apps({ selected, onPick }: { selected: string; onPick: (id: string) => void }) {
  const { copy, feedback } = useI18n();
  const tell = useToast();
  const path = "/api/aplicativos";
  const cached = peek<Aplicativo[]>(path, "session");
  const [rows, setRows] = useState<Aplicativo[] | undefined>(cached);
  const [nome, setNome] = useState("");

  useEffect(() => {
    if (cached) {
      return;
    }
    load<Aplicativo[]>(path, "session").then(setRows).catch((reason) => tell(feedback(reason), "err"));
  }, [cached, tell, feedback]);

  async function create(event: FormEvent) {
    event.preventDefault();
    try {
      await post(path, "session", { nome });
      setNome("");
      setRows(await load<Aplicativo[]>(path, "session"));
      tell(copy.actions.create);
    } catch (reason) {
      tell(feedback(reason), "err");
    }
  }

  async function rotate(id: string) {
    try {
      await post(path + "/" + id + "/rotacao", "session", {});
      setRows(await load<Aplicativo[]>(path, "session"));
    } catch (reason) {
      tell(feedback(reason), "err");
    }
  }

  return (
    <Page title={copy.nav.apps}>
      <form className="mb-6 grid gap-3 md:grid-cols-[1fr_auto]" onSubmit={create}>
        <Field label={copy.fields.name}>
          <input className={control} value={nome} onChange={(event) => setNome(event.target.value)} />
        </Field>
        <button className="acao self-end rounded-2xl px-4 py-2 text-sm" type="submit">{copy.actions.create}</button>
      </form>
      {rows === undefined ? <Skeleton /> : rows.length === 0 ? <p className="cartao text-sm tinta-apoio">{copy.empty}</p> : (
        <div className="grid gap-4">
          {rows.map((item) => (
            <article key={item.id} className={selected === item.id ? "cartao p-4 ring-1 ring-orange-500" : "cartao p-4"}>
              <div className="mb-3 flex items-center justify-between gap-3">
                <h2 className="text-lg">{item.nome}</h2>
                <button className="text-sm underline" type="button" onClick={() => onPick(item.id)}>{item.nome}</button>
              </div>
              <dl className="grid gap-1 text-sm">
                <div><dt className="inline tinta-apoio">{copy.creds.host}: </dt><dd className="inline">{item.hostSmtp}</dd></div>
                <div><dt className="inline tinta-apoio">{copy.creds.port}: </dt><dd className="inline">{item.portaSmtp}</dd></div>
                <div><dt className="inline tinta-apoio">{copy.creds.user}: </dt><dd className="inline">{item.usuarioSmtp}</dd></div>
                <div><dt className="inline tinta-apoio">{copy.creds.password}: </dt><dd className="inline">{item.senhaSmtp}</dd></div>
                <div><dt className="inline tinta-apoio">{copy.creds.token}: </dt><dd className="inline break-all">{item.tokenAuth}</dd></div>
              </dl>
              <button className="mt-3 text-sm underline" type="button" onClick={() => rotate(item.id)}>{copy.actions.rotate}</button>
            </article>
          ))}
        </div>
      )}
    </Page>
  );
}

function Destinations({ appId }: { appId: string }) {
  const { copy, feedback } = useI18n();
  const tell = useToast();
  const path = "/api/destinos?aplicativo=" + appId;
  const [rows, setRows] = useState<Destino[] | undefined>(appId ? peek<Destino[]>(path, "session") : []);
  const [canal, setCanal] = useState("email");
  const [provedor, setProvedor] = useState("sendgrid");
  const [nome, setNome] = useState("");
  const [segredo, setSegredo] = useState("");

  useEffect(() => {
    if (!appId) {
      setRows([]);
      return;
    }
    const hit = peek<Destino[]>(path, "session");
    if (hit) {
      setRows(hit);
      return;
    }
    setRows(undefined);
    load<Destino[]>(path, "session").then(setRows).catch((reason) => tell(feedback(reason), "err"));
  }, [appId, path, tell, feedback]);

  async function add(event: FormEvent) {
    event.preventDefault();
    try {
      await post("/api/destinos", "session", { aplicativoId: appId, canal, provedor, nome, segredo });
      setNome("");
      setSegredo("");
      setRows(await load<Destino[]>(path, "session"));
    } catch (reason) {
      tell(feedback(reason), "err");
    }
  }

  async function move(index: number, delta: number) {
    const next = rows ? [...rows] : [];
    const alvo = index + delta;
    if (!next[index] || !next[alvo]) {
      return;
    }
    const [item] = next.splice(index, 1);
    next.splice(alvo, 0, item);
    await save("/api/destinos/ordem", "session", { aplicativoId: appId, canal: item.canal, ids: next.filter((row) => row.canal === item.canal).map((row) => row.id) });
    setRows(await load<Destino[]>(path, "session"));
  }

  return (
    <Page title={copy.nav.destinations} lede={appId || copy.empty}>
      <form className="mb-6 grid gap-3" onSubmit={add}>
        <Field label={copy.fields.channel}>
          <select className={control} value={canal} onChange={(event) => setCanal(event.target.value)}>
            <option value="email">email</option>
            <option value="sms">sms</option>
          </select>
        </Field>
        <Field label={copy.fields.provider}>
          <select className={control} value={provedor} onChange={(event) => setProvedor(event.target.value)}>
            <option value="sendgrid">sendgrid</option>
            <option value="mailgrid">mailgrid</option>
            <option value="twilio">twilio</option>
            <option value="falso">falso</option>
          </select>
        </Field>
        <Field label={copy.fields.name}>
          <input className={control} value={nome} onChange={(event) => setNome(event.target.value)} />
        </Field>
        <Field label={copy.fields.secret}>
          <input className={control} value={segredo} onChange={(event) => setSegredo(event.target.value)} />
        </Field>
        <button className="acao w-fit rounded-2xl px-4 py-2 text-sm" type="submit" disabled={!appId}>{copy.actions.addDestination}</button>
      </form>
      {rows === undefined ? <Skeleton /> : rows.length === 0 ? <p className="cartao text-sm tinta-apoio">{copy.empty}</p> : (
        <ol className="grid gap-2">
          {rows.map((item, index) => (
            <li key={item.id} className="cartao flex items-center justify-between gap-3 p-3 text-sm">
              <span>{item.ordem}. {item.nome} · {item.canal} · {item.provedor}</span>
              <span>
                <button className="mr-3 underline" type="button" onClick={() => move(index, -1)}>{copy.actions.up}</button>
                <button className="mr-3 underline" type="button" onClick={() => move(index, 1)}>{copy.actions.down}</button>
                <button className="underline" type="button" onClick={() => { remove("/api/destinos/" + item.id, "session").then(() => load<Destino[]>(path, "session")).then(setRows).catch((reason) => tell(feedback(reason), "err")); }}>{copy.actions.remove}</button>
              </span>
            </li>
          ))}
        </ol>
      )}
    </Page>
  );
}

function Domains({ appId }: { appId: string }) {
  const { copy, feedback } = useI18n();
  const tell = useToast();
  const path = "/api/dominios?aplicativo=" + appId;
  const [rows, setRows] = useState<Dominio[] | undefined>(undefined);

  useEffect(() => {
    if (!appId) {
      setRows([]);
      return;
    }
    const hit = peek<Dominio[]>(path, "session");
    if (hit) {
      setRows(hit);
      return;
    }
    setRows(undefined);
    load<Dominio[]>(path, "session").then(setRows).catch((reason) => tell(feedback(reason), "err"));
  }, [appId, path, tell, feedback]);

  return (
    <Page title={copy.nav.domains}>
      {rows === undefined ? <Skeleton /> : rows.length === 0 ? <p className="cartao text-sm tinta-apoio">{copy.empty}</p> : (
        <ul className="grid gap-2">
          {rows.map((item) => (
            <li key={item.dominio} className="cartao p-3 text-sm">{item.dominio} · {item.validado} · {item.liberado}</li>
          ))}
        </ul>
      )}
    </Page>
  );
}

function Deliveries({ appId }: { appId: string }) {
  const { copy, feedback } = useI18n();
  const tell = useToast();
  const path = "/api/envios?aplicativo=" + appId;
  const [rows, setRows] = useState<Envio[] | undefined>(undefined);

  useEffect(() => {
    if (!appId) {
      setRows([]);
      return;
    }
    const hit = peek<Envio[]>(path, "session");
    if (hit) {
      setRows(hit);
      return;
    }
    setRows(undefined);
    load<Envio[]>(path, "session").then(setRows).catch((reason) => tell(feedback(reason), "err"));
  }, [appId, path, tell, feedback]);

  return (
    <Page title={copy.nav.deliveries}>
      {rows === undefined ? <Skeleton /> : rows.length === 0 ? <p className="cartao text-sm tinta-apoio">{copy.empty}</p> : (
        <ul className="grid gap-2">
          {rows.map((item) => (
            <li key={item.mensagemId + item.destinatario} className="cartao p-3 text-sm">
              {item.quando} · {item.remetente} → {item.destinatario} · {item.status}
            </li>
          ))}
        </ul>
      )}
    </Page>
  );
}

function Health() {
  const { copy, feedback } = useI18n();
  const tell = useToast();
  const [body, setBody] = useState<string>("");

  useEffect(() => {
    fetch("/health/complete")
      .then(async (response) => {
        setBody(JSON.stringify(await response.json(), null, 2));
      })
      .catch((reason) => tell(feedback(reason), "err"));
  }, [tell, feedback]);

  return (
    <Page title={copy.nav.health}>
      {body ? <pre className="cartao overflow-auto p-4 text-xs">{body}</pre> : <Skeleton />}
    </Page>
  );
}
