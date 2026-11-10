// Servidor de salas do campeonato online do Autorama Creator (seção "Campeonato online" do guia).
// Só repassa mensagens: guarda as salas, os nomes, as cores, o chat e a lista de pistas escolhida pelo host.
// Uso: npm install && npm start   (porta em PORT, padrão 8080)
'use strict';
const http = require('http');
const { WebSocketServer } = require('ws');

const PROTOCOL = 1;                    // muda quando as mensagens deixam de combinar com o cliente (OnlineClient.Protocol)
const MAX_PLAYERS = 8;                 // o mesmo limite do campeonato
const MAX_STAGES = 12;
const MAX_TRACK_JSON = 100 * 1024;     // por pista
const MAX_ROOMS = 500;
const CHAT_MAX = 200, CHAT_KEEP = 50;
const NAME_MAX = 20, ROOM_MAX = 30;
const CHAT_RATE = { n: 5, ms: 3000 };  // 5 mensagens a cada 3 s
const PING_MS = 15000;
const MAX_RELAY = 16 * 1024;           // snapshot ou evento da corrida
const IN_RATE = 120;                   // entradas (botão) por segundo de cada visitante
const MAX_CONN_IP = 20;                // conexões abertas ao mesmo tempo por endereço
const MAX_CREATES_IP = 6;              // salas criadas por minuto por endereço
const CODE_CHARS = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';   // sem 0/O/1/I
const COLORS = ['#E0362C', '#2563EB', '#F2B705', '#16A34A', '#9333EA', '#F97316', '#0EA5B7', '#E04B95'];   // PILOT_COLORS do guia

// Tira controle e espaços repetidos; nome vazio vira ''.
const clean = (s, max) => String(s == null ? '' : s).replace(/[\u0000-\u001f\u007f]/g, ' ').replace(/\s+/g, ' ').trim().slice(0, max);

// trustProxy: atrás de Fly.io/Caddy/nginx o endereço do jogador vem no cabeçalho, não no socket.
function criarServidor({ port = 8080, host = '0.0.0.0', trustProxy = false, maxConnPerIp = MAX_CONN_IP, maxCreatesPerMin = MAX_CREATES_IP } = {}) {
  const rooms = new Map();             // code -> room
  const conns = new Map();             // ip -> conexões abertas
  const creates = new Map();           // ip -> horas das últimas salas criadas
  let nextId = 1;
  const ipOf = req => {
    const fwd = trustProxy && (req.headers['fly-client-ip'] || String(req.headers['x-forwarded-for'] || '').split(',')[0].trim());
    return fwd || req.socket.remoteAddress || '?';
  };

  const http_ = http.createServer((req, res) => {
    if (req.url === '/saude') { res.writeHead(200, { 'content-type': 'text/plain' }); res.end(`ok salas=${rooms.size}`); return; }
    res.writeHead(404); res.end();
  });
  const wss = new WebSocketServer({ server: http_, maxPayload: 2 * 1024 * 1024 });

  const send = (c, msg) => { if (c.ws.readyState === 1) c.ws.send(JSON.stringify(msg)); };
  const fail = (c, code, text) => send(c, { t: 'error', code, text });
  const snapshot = r => ({
    code: r.code, name: r.name, pub: r.pub, host: r.host, max: MAX_PLAYERS, state: r.state,
    players: r.players.map(p => ({ id: p.id, name: p.name, color: p.color, ready: p.id === r.host || !!p.ready })),   // o host está sempre pronto
    stages: r.stages.map(s => ({ name: s.name, laps: s.laps, secs: s.secs, tempo: s.tempo, faixas: s.faixas, hash: s.hash })),
  });
  const broadcast = (r, msg) => r.players.forEach(p => send(p, msg));
  const pushRoom = r => broadcast(r, { t: 'room', room: snapshot(r) });
  const say = (r, text) => {
    const m = { t: 'chat', sys: true, text, time: Date.now() };
    r.chat.push(m); if (r.chat.length > CHAT_KEEP) r.chat.shift();
    broadcast(r, m);
  };

  function newCode() {
    for (;;) {
      let s = ''; for (let i = 0; i < 5; i++) s += CODE_CHARS[Math.floor(Math.random() * CODE_CHARS.length)];
      if (!rooms.has(s)) return s;
    }
  }
  const freeColor = r => COLORS.find(c => !r.players.some(p => p.color === c));

  function validName(c, name) {
    const n = clean(name, NAME_MAX);
    if (!n) { fail(c, 'nome', 'Digite seu nome.'); return null; }
    return n;
  }

  function enter(c, r, name) {
    c.name = name; c.color = freeColor(r); c.room = r;
    r.players.push(c);
    send(c, { t: 'joined', you: c.id, room: snapshot(r), chat: r.chat });
    say(r, `${name} entrou na sala.`);
    pushRoom(r);
  }

  // Campeonato acabou (ou foi interrompido): todos voltam ao lobby e precisam confirmar "pronto" de novo.
  function backToLobby(r, text) {
    r.state = 'lobby';
    r.players.forEach(p => { p.ready = false; });
    broadcast(r, { t: 'lobby', text });
    pushRoom(r);
  }

  function leave(c) {
    const r = c.room; if (!r) return;
    c.room = null;
    r.players = r.players.filter(p => p !== c);
    if (!r.players.length) { rooms.delete(r.code); return; }
    say(r, `${c.name} saiu da sala.`);
    if (r.host === c.id) {
      r.host = r.players[0].id; say(r, `${r.players[0].name} agora é o host.`);
      // Quem roda a corrida era o host: sem ele não dá para continuar.
      if (r.state === 'racing') return backToLobby(r, 'O host saiu: o campeonato foi encerrado.');
    }
    pushRoom(r);
  }

  // Corrida: o host manda snapshots e eventos para todos; os outros só mandam o botão, e só para o host.
  const forward = (c, raw) => {
    const r = c.room;
    if (!r || r.state !== 'racing' || r.host !== c.id || raw.length > MAX_RELAY) return;
    r.players.forEach(p => { if (p !== c) p.ws.readyState === 1 && p.ws.send(raw); });
  };

  const handlers = {
    list(c) {
      const out = [...rooms.values()].filter(r => r.pub && r.state === 'lobby').map(r => ({
        code: r.code, name: r.name, host: r.players.find(p => p.id === r.host).name, players: r.players.length, max: MAX_PLAYERS, stages: r.stages.length,
      }));
      send(c, { t: 'rooms', rooms: out });
    },
    // A primeira mensagem de todo cliente: a versão do protocolo. Versão diferente: avisa e derruba.
    hello(c, m) {
      if (Number(m.ver) !== PROTOCOL) { fail(c, 'versao', 'Esta versão do jogo não combina com o servidor. Atualize o jogo.'); return c.ws.close(); }
      c.ok = true;
    },
    create(c, m) {
      if (c.room) return fail(c, 'sala', 'Você já está numa sala.');
      const name = validName(c, m.name); if (!name) return;
      if (rooms.size >= MAX_ROOMS) return fail(c, 'cheio', 'O servidor está lotado. Tente mais tarde.');
      const now = Date.now(), recent = (creates.get(c.ip) || []).filter(t => now - t < 60000);
      if (recent.length >= maxCreatesPerMin) return fail(c, 'muitas-salas', 'Você criou muitas salas em pouco tempo. Espere um minuto.');
      recent.push(now); creates.set(c.ip, recent);
      const r = { code: newCode(), name: clean(m.roomName, ROOM_MAX) || `Sala de ${name}`, pub: !!m.pub, host: c.id, state: 'lobby', players: [], stages: [], chat: [] };
      rooms.set(r.code, r);
      enter(c, r, name);
    },
    join(c, m) {
      if (c.room) return fail(c, 'sala', 'Você já está numa sala.');
      const name = validName(c, m.name); if (!name) return;
      const r = rooms.get(clean(m.code, 8).toUpperCase());
      if (!r) return fail(c, 'sem-sala', 'Sala não encontrada. Confira o código.');
      if (r.state !== 'lobby') return fail(c, 'andamento', 'O campeonato dessa sala já começou.');
      if (r.players.length >= MAX_PLAYERS) return fail(c, 'cheia', 'A sala está cheia.');
      if (r.players.some(p => p.name.toLowerCase() === name.toLowerCase())) return fail(c, 'nome-usado', 'Já tem alguém com esse nome na sala.');
      enter(c, r, name);
    },
    leave(c) { leave(c); send(c, { t: 'left' }); },
    // Pronto para largar. O host está sempre pronto; mexer nas pistas (ou chegar alguém) zera os outros.
    ready(c, m) {
      const r = c.room; if (!r) return;
      if (r.state !== 'lobby') return fail(c, 'andamento', 'O campeonato já começou.');
      if (r.host === c.id) return;
      c.ready = !!m.ready; pushRoom(r);
    },
    // Só o host, com todos prontos: começa o campeonato e entrega a todos as pistas (JSON) e as regras.
    start(c, m) {
      const r = c.room; if (!r) return;
      if (r.host !== c.id) return fail(c, 'host', 'Só o host começa o campeonato.');
      if (r.state !== 'lobby') return fail(c, 'andamento', 'O campeonato já começou.');
      if (r.players.length < 2) return fail(c, 'poucos', 'Precisa de pelo menos 2 pilotos.');
      if (!r.stages.length) return fail(c, 'sem-etapas', 'Escolha pelo menos uma pista.');
      const falta = r.players.filter(p => p !== c && !p.ready);
      if (falta.length) return fail(c, 'nao-prontos', `Falta ficar pronto: ${falta.map(p => p.name).join(', ')}.`);
      const rules = { vmax: Number(m.vmax), ramp: Number(m.ramp), grip: Number(m.grip), collide: !!m.collide, byTime: !!m.byTime };
      if (!(rules.vmax >= 8 && rules.vmax <= 20 && rules.ramp >= 0.2 && rules.ramp <= 1.5 && rules.grip >= 15 && rules.grip <= 45)) return fail(c, 'regras', 'Regras inválidas.');
      r.state = 'racing';
      const title = clean(m.name, 40) || `Campeonato de ${c.name}`;
      const msg = { t: 'started', title, rules, pilots: snapshot(r).players, stages: r.stages.map(s => ({ name: s.name, laps: s.laps, secs: s.secs, tempo: s.tempo, faixas: s.faixas, hash: s.hash, json: s.json })) };
      broadcast(r, msg);
      say(r, `${c.name} largou o campeonato "${title}".`);
      pushRoom(r);
    },
    // Host: acabou (ou desistiu). Todos voltam ao lobby.
    end(c, m) {
      const r = c.room; if (!r || r.state !== 'racing') return;
      if (r.host !== c.id) return fail(c, 'host', 'Só o host encerra o campeonato.');
      backToLobby(r, clean(m.text, 100) || 'O campeonato terminou.');
    },
    // Visitante → host: o estado do botão (held) e o contador de "recolocar" (re).
    in(c, m) {
      const r = c.room; if (!r || r.state !== 'racing' || r.host === c.id) return;
      const now = Date.now();
      c.inTimes = (c.inTimes || []).filter(t => now - t < 1000);
      if (c.inTimes.length >= IN_RATE) return;
      c.inTimes.push(now);
      const host = r.players.find(p => p.id === r.host);
      // gp: nível do acelerador em % (gatilho do controle ou tecla de meia). Ausente = 100.
      const gp = Math.max(1, Math.min(100, Math.round(Number(m.gp)) || 100));
      if (host) send(host, { t: 'in', from: c.id, held: !!m.held, gp, re: Math.max(0, Math.min(1e6, Math.round(Number(m.re)) || 0)) });
    },
    color(c, m) {
      const r = c.room; if (!r) return;
      if (r.state !== 'lobby') return fail(c, 'andamento', 'O campeonato já começou.');
      const col = String(m.color || '').toUpperCase();
      if (!COLORS.includes(col)) return fail(c, 'cor', 'Cor inválida.');
      if (r.players.some(p => p !== c && p.color === col)) return fail(c, 'cor-usada', 'Essa cor já é de outro piloto.');
      c.color = col; pushRoom(r);
    },
    chat(c, m) {
      const r = c.room; if (!r) return;
      const text = clean(m.text, CHAT_MAX); if (!text) return;
      const now = Date.now();
      c.chatTimes = (c.chatTimes || []).filter(t => now - t < CHAT_RATE.ms);
      if (c.chatTimes.length >= CHAT_RATE.n) return fail(c, 'rapido', 'Calma, você está mandando mensagens rápido demais.');
      c.chatTimes.push(now);
      const msg = { t: 'chat', from: c.id, name: c.name, color: c.color, text, time: now };
      r.chat.push(msg); if (r.chat.length > CHAT_KEEP) r.chat.shift();
      broadcast(r, msg);
    },
    // Só o host: escolhe as pistas (nome, voltas, identidade e o JSON da pista, guardado para a largada).
    stages(c, m) {
      const r = c.room; if (!r) return;
      if (r.host !== c.id) return fail(c, 'host', 'Só o host escolhe as pistas.');
      if (r.state !== 'lobby') return fail(c, 'andamento', 'O campeonato já começou.');
      if (!Array.isArray(m.stages) || m.stages.length > MAX_STAGES) return fail(c, 'etapas', `No máximo ${MAX_STAGES} etapas.`);
      const out = [];
      for (const s of m.stages) {
        const laps = Math.round(Number(s && s.laps));
        const secs = s && s.secs != null && Number(s.secs) !== 0 ? Math.round(Number(s.secs)) : 120;   // corrida por tempo (cliente antigo: 120)
        if (!s || !/^[0-9a-f]{8}$/.test(String(s.hash)) || !(laps >= 1 && laps <= 30) || !(secs >= 60 && secs <= 600) || typeof s.json !== 'string' || !s.json || s.json.length > MAX_TRACK_JSON)
          return fail(c, 'etapas', 'Etapa inválida ou pista grande demais.');
        // tempo: o formato que o host escolheu (por tempo); faixas: pista de 2 ou 4 carros. Só repassados para os visitantes verem.
        out.push({ name: clean(s.name, 60) || 'Pista', laps, secs, tempo: !!s.tempo, faixas: Number(s.faixas) === 4 ? 4 : 2, hash: s.hash, json: s.json });
      }
      const mudou = JSON.stringify(out.map(s => [s.hash, s.laps, s.secs, s.tempo])) !== JSON.stringify(r.stages.map(s => [s.hash, s.laps, s.secs, s.tempo]));
      r.stages = out;
      if (mudou && r.players.some(p => p !== c && p.ready)) { r.players.forEach(p => { p.ready = false; }); say(r, 'O host mudou as etapas: confirmem "pronto" de novo.'); }
      pushRoom(r);
    },
    visibility(c, m) {
      const r = c.room; if (!r) return;
      if (r.host !== c.id) return fail(c, 'host', 'Só o host muda a visibilidade.');
      r.pub = !!m.pub; pushRoom(r);
    },
    ping(c) { send(c, { t: 'pong' }); },
    snap(c, m, raw) { forward(c, raw); },
    ev(c, m, raw) { forward(c, raw); },
  };

  wss.on('connection', (ws, req) => {
    const c = { ws, id: String(nextId++), room: null, ip: ipOf(req), ok: false };
    const n = (conns.get(c.ip) || 0) + 1;
    conns.set(c.ip, n);
    ws.on('close', () => { const k = (conns.get(c.ip) || 1) - 1; if (k > 0) conns.set(c.ip, k); else conns.delete(c.ip); });
    if (n > maxConnPerIp) { fail(c, 'muitas-conexoes', 'Muitas conexões deste endereço.'); ws.close(); return; }
    ws.alive = true;
    ws.on('pong', () => { ws.alive = true; });
    ws.on('message', (data, isBinary) => {
      ws.alive = true;
      if (isBinary) return;
      let raw = data.toString(), m; try { m = JSON.parse(raw); } catch (e) { return fail(c, 'json', 'Mensagem inválida.'); }
      const h = m && Object.prototype.hasOwnProperty.call(handlers, m.t) ? handlers[m.t] : null;
      if (!h) return fail(c, 'tipo', 'Mensagem desconhecida.');
      if (!c.ok && m.t !== 'hello') { fail(c, 'versao', 'Esta versão do jogo não combina com o servidor. Atualize o jogo.'); return ws.close(); }
      try { h(c, m, raw); } catch (e) { console.error(e); fail(c, 'erro', 'Erro no servidor.'); }
    });
    ws.on('close', () => leave(c));
    ws.on('error', () => {});
    send(c, { t: 'hello', id: c.id, ver: PROTOCOL });
  });

  // Quem não responde a um ping (nem manda nada) em um ciclo inteiro é derrubado, e sai da sala.
  const beat = setInterval(() => wss.clients.forEach(ws => {
    if (!ws.alive) { ws.terminate(); return; }
    ws.alive = false; ws.ping();
  }), PING_MS);

  return new Promise(resolve => http_.listen(port, host, () => resolve({
    port: http_.address().port, rooms,
    close: () => new Promise(done => { clearInterval(beat); wss.clients.forEach(w => w.terminate()); wss.close(() => http_.close(() => done())); }),
  })));
}

module.exports = { criarServidor, COLORS, MAX_PLAYERS, PROTOCOL };

if (require.main === module) {
  criarServidor({ port: Number(process.env.PORT) || 8080, trustProxy: process.env.TRUST_PROXY === '1' }).then(s => console.log(`Autorama: servidor de salas em ws://localhost:${s.port}`));
}
