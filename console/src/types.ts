export type Cell = string | number | null;

export type Aplicativo = {
  id: string;
  nome: string;
  hostSmtp: string;
  portaSmtp: number;
  usuarioSmtp: string;
  senhaSmtp: string;
  tokenAuth: string;
  criadoEm: string;
};

export type Destino = {
  id: string;
  aplicativoId: string;
  canal: string;
  provedor: string;
  ordem: number;
  nome: string;
};

export type Dominio = {
  dominio: string;
  validado: string;
  liberado: string;
};

export type Envio = {
  mensagemId: string;
  msgId: string;
  remetente: string;
  destinatario: string;
  status: string;
  codigo: string;
  quando: string;
  canal: string;
};

export type Sessao = { nome: string };
