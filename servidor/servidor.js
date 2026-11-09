// Servidor de salas do campeonato online do Autorama Creator (seção "Campeonato online" do guia).
// Só repassa mensagens: guarda as salas, os nomes, as cores, o chat e a lista de pistas escolhida pelo host.
// Uso: npm install && npm start   (porta em PORT, padrão 8080)
'use strict';
const http = require('http');
const { WebSocketServer } = require('ws');

const MAX_PLAYERS = 8;                 // o mesmo limite do campeonato
const MAX_STAGES = 12;
const MAX_TRACK_JSON = 100 * 1024;     // por pista
const MAX_ROOMS = 500;
const CHAT_MAX = 200, CHAT_KEEP = 50;
const NAME_MAX = 20, ROOM_MAX = 30;
const CHAT_RATE = { n: 5, ms: 3000 };  // 5 mensagens a cada 3 s
const PING_MS = 15000;
const CODE_CHARS = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';   // sem 0/O/1/I
const COLORS = ['#E0362C', '#2563EB', '#F2B705', '#16A34A', '#9333EA', '#F97316', '#0EA5B7', '#E04B95'];   // PILOT_COLORS do guia

// Tira controle e espaços repetidos; nome vazio vira ''.
const clean = (s, max) => String(s == null ? '' : s).replace(/[\u0000-\u001f\u007f]/g, ' ').replace(/\s+/g, ' ').trim().slice(0, max);

function criarServidor({ port = 8080, host = '0.0.0.0' } = {}) {
  const rooms = new Map();             // code -> room
  let nextId = 1;

  const http_ = http.createServer((req, res) => {
    if (req.url === '/saude') { res.writeHead(200, { 'content-type': 'text/plain' }); res.end(`ok salas=${rooms.size}`); return; }
    res.writeHead(404); res.end();
  });
  const wss = new WebSocketServer({ server: http_, maxPayload: 2 * 1024 * 1024 });

  const send = (c, msg) => { if (c.ws.readyState === 1) c.ws.send(JSON.stringify(msg)); };
  const fail = (c, code, text) => send(c, { t: 'error', code, text });
  const snapshot = r => ({
    code: r.code, name: r.name, pub: r.pub, host: r.host, max: MAX_PLAYERS, state: r.state,
    players: r.players.map(p => ({ id: p.id, name: p.name, color: p.color })),
    stages: r.stages.map(s => ({ name: s.name, laps: s.laps, hash: s.hash })),
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

  function leave(c) {
    const r = c.room; if (!r) return;
    c.room = null;
    r.players = r.players.filter(p => p !== c);
    if (!r.players.length) { rooms.delete(r.code); return; }
    say(r, `${c.name} saiu da sala.`);
    if (r.host === c.id) { r.host = r.players[0].id; say(r, `${r.players[0].name} agora é o host.`); }
    pushRoom(r);
  }

  const handlers = {
    list(c) {
      const out = [...rooms.values()].filter(r => r.pub && r.state === 'lobby').map(r => ({
        code: r.code, name: r.name, host: r.players.find(p => p.id === r.host).name, players: r.players.length, max: MAX_PLAYERS, stages: r.stages.length,
      }));
      send(c, { t: 'rooms', rooms: out });
    },
    create(c, m) {
      if (c.room) return fail(c, 'sala', 'Você já está numa sala.');
      const name = validName(c, m.name); if (!name) return;
      if (rooms.size >= MAX_ROOMS) return fail(c, 'cheio', 'O servidor está lotado. Tente mais tarde.');
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
    color(c, m) {
      const r = c.room; if (!r) return;
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
      if (!Array.isArray(m.stages) || m.stages.length > MAX_STAGES) return fail(c, 'etapas', `No máximo ${MAX_STAGES} etapas.`);
      const out = [];
      for (const s of m.stages) {
        const laps = Math.round(Number(s && s.laps));
        if (!s || !/^[0-9a-f]{8}$/.test(String(s.hash)) || !(laps >= 1 && laps <= 30) || typeof s.json !== 'string' || !s.json || s.json.length > MAX_TRACK_JSON)
          return fail(c, 'etapas', 'Etapa inválida ou pista grande demais.');
        out.push({ name: clean(s.name, 60) || 'Pista', laps, hash: s.hash, json: s.json });
      }
      r.stages = out; pushRoom(r);
    },
    visibility(c, m) {
      const r = c.room; if (!r) return;
      if (r.host !== c.id) return fail(c, 'host', 'Só o host muda a visibilidade.');
      r.pub = !!m.pub; pushRoom(r);
    },
    ping(c) { send(c, { t: 'pong' }); },
  };

  wss.on('connection', ws => {
    const c = { ws, id: String(nextId++), room: null };
    ws.alive = true;
    ws.on('pong', () => { ws.alive = true; });
    ws.on('message', (data, isBinary) => {
      ws.alive = true;
      if (isBinary) return;
      let m; try { m = JSON.parse(data.toString()); } catch (e) { return fail(c, 'json', 'Mensagem inválida.'); }
      const h = m && Object.prototype.hasOwnProperty.call(handlers, m.t) ? handlers[m.t] : null;
      if (!h) return fail(c, 'tipo', 'Mensagem desconhecida.');
      try { h(c, m); } catch (e) { console.error(e); fail(c, 'erro', 'Erro no servidor.'); }
    });
    ws.on('close', () => leave(c));
    ws.on('error', () => {});
    send(c, { t: 'hello', id: c.id });
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

module.exports = { criarServidor, COLORS, MAX_PLAYERS };

if (require.main === module) {
  criarServidor({ port: Number(process.env.PORT) || 8080 }).then(s => console.log(`Autorama: servidor de salas em ws://localhost:${s.port}`));
}
