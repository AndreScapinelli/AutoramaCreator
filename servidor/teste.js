// Teste do servidor de salas: node teste.js
'use strict';
const assert = require('assert');
const WebSocket = require('ws');
const { criarServidor, COLORS, MAX_PLAYERS, PROTOCOL } = require('./servidor');

function cliente(port, semHello) {
  const ws = new WebSocket(`ws://127.0.0.1:${port}`);
  const inbox = [], waiters = [];
  ws.on('message', d => {
    const m = JSON.parse(d.toString());
    const i = waiters.findIndex(w => w.pred(m));
    if (i >= 0) waiters.splice(i, 1)[0].res(m); else inbox.push(m);
  });
  const c = {
    ws,
    open: () => new Promise(r => ws.on('open', r)),
    send: m => ws.send(JSON.stringify(m)),
    // espera a próxima mensagem do tipo t (as que já chegaram ficam na fila)
    next: (t, ms = 1500) => new Promise((res, rej) => {
      const pred = m => m.t === t;
      const i = inbox.findIndex(pred);
      if (i >= 0) return res(inbox.splice(i, 1)[0]);
      const w = { pred, res }; waiters.push(w);
      setTimeout(() => { const j = waiters.indexOf(w); if (j >= 0) { waiters.splice(j, 1); rej(new Error('sem mensagem ' + t)); } }, ms);
    }),
    // confere que NÃO chegou mensagem do tipo t
    quiet: async (t, ms = 150) => { await new Promise(r => setTimeout(r, ms)); assert(!inbox.some(m => m.t === t), 'chegou ' + t); },
    drain: () => { inbox.length = 0; },
    close: () => ws.close(),
  };
  return c.open().then(() => { if (semHello !== true) c.send({ t: 'hello', ver: PROTOCOL }); return c.next('hello'); }).then(() => c);
}

const trilha = (n, extra = {}) => ({ name: 'Pista ' + n, laps: 3, hash: 'a34f4cb9', json: '{"nome":"x"}', ...extra });

(async () => {
  const srv = await criarServidor({ port: 0, host: '127.0.0.1' });
  const P = srv.port;
  const ok = n => console.log('ok  ' + n);

  const host = await cliente(P), g1 = await cliente(P), g2 = await cliente(P);

  // criar exige nome
  host.send({ t: 'create', name: '   ', pub: true });
  assert.strictEqual((await host.next('error')).code, 'nome'); ok('criar sem nome é recusado');

  host.send({ t: 'create', name: '  Ana  ', roomName: 'Sala da Ana', pub: true });
  let j = await host.next('joined');
  assert.strictEqual(j.room.players[0].name, 'Ana');
  assert.strictEqual(j.room.host, j.you);
  assert.strictEqual(j.room.players[0].color, COLORS[0]);
  assert(/^[A-Z2-9]{5}$/.test(j.room.code));
  const code = j.room.code;
  ok('criar sala pública: código ' + code);

  // lista pública
  g1.send({ t: 'list' });
  let l = await g1.next('rooms');
  assert.strictEqual(l.rooms.length, 1);
  assert.strictEqual(l.rooms[0].code, code);
  assert.strictEqual(l.rooms[0].host, 'Ana'); ok('sala pública aparece na lista');

  // entrar exige nome e código válido
  g1.send({ t: 'join', name: '', code });
  assert.strictEqual((await g1.next('error')).code, 'nome');
  g1.send({ t: 'join', name: 'Beto', code: 'ZZZZZ' });
  assert.strictEqual((await g1.next('error')).code, 'sem-sala'); ok('entrar sem nome / sala inexistente é recusado');

  g1.send({ t: 'join', name: 'Beto', code: code.toLowerCase() });
  j = await g1.next('joined');
  assert.strictEqual(j.room.players.length, 2);
  assert.strictEqual(j.room.players[1].color, COLORS[1]);
  await host.next('room'); ok('visitante entra pelo código (sem diferenciar maiúsculas)');

  // nome repetido
  g2.send({ t: 'join', name: 'beto', code });
  assert.strictEqual((await g2.next('error')).code, 'nome-usado'); ok('nome repetido na sala é recusado');

  // cor: só da paleta e única
  g1.drain(); host.drain();
  g1.send({ t: 'color', color: '#123456' });
  assert.strictEqual((await g1.next('error')).code, 'cor');
  g1.send({ t: 'color', color: COLORS[0] });
  assert.strictEqual((await g1.next('error')).code, 'cor-usada');
  g1.send({ t: 'color', color: COLORS[5].toLowerCase() });
  let r = await host.next('room');
  assert.strictEqual(r.room.players[1].color, COLORS[5]); ok('visitante escolhe a cor (única, da paleta)');

  // chat chega para todos, com nome e cor
  host.drain(); g1.drain();
  g1.send({ t: 'chat', text: '  oi   pessoal  ' });
  let a = await host.next('chat'), b = await g1.next('chat');
  assert.strictEqual(a.text, 'oi pessoal'); assert.strictEqual(a.name, 'Beto'); assert.strictEqual(a.color, COLORS[5]);
  assert.deepStrictEqual(a, b); ok('chat chega para todos');
  g1.send({ t: 'chat', text: '   ' }); await host.quiet('chat'); ok('chat vazio é ignorado');

  // limite de ritmo
  for (let i = 0; i < 6; i++) g1.send({ t: 'chat', text: 'spam ' + i });
  assert.strictEqual((await g1.next('error')).code, 'rapido'); ok('chat limita o ritmo');
  await new Promise(r => setTimeout(r, 100)); g1.drain();

  // pistas: só o host
  g1.send({ t: 'stages', stages: [trilha(1)] });
  assert.strictEqual((await g1.next('error')).code, 'host'); ok('visitante não escolhe pistas');
  host.drain(); g1.drain();
  host.send({ t: 'stages', stages: [trilha(1), trilha(2, { laps: 5 })] });
  r = await g1.next('room');
  assert.deepStrictEqual(r.room.stages.map(s => [s.name, s.laps]), [['Pista 1', 3], ['Pista 2', 5]]);
  assert(!('json' in r.room.stages[0])); ok('host escolhe pistas; o JSON não vai no resumo');
  host.send({ t: 'stages', stages: [trilha(1, { secs: 90, tempo: true, faixas: 4 }), trilha(2, { laps: 5 })] });
  r = await g1.next('room');
  assert.deepStrictEqual(r.room.stages.map(s => [s.secs, s.tempo, s.faixas]), [[90, true, 4], [120, false, 2]]);
  ok('duração, formato e tipo de pista chegam aos visitantes (sem o campo: 120 s, por voltas, 2 carros)');
  host.send({ t: 'stages', stages: [trilha(1, { secs: 30 })] });
  assert.strictEqual((await host.next('error')).code, 'etapas'); ok('duração fora de 1 a 10 min é recusada');
  host.send({ t: 'stages', stages: [trilha(1), trilha(2, { laps: 5 })] }); await g1.next('room');
  host.send({ t: 'stages', stages: [trilha(1, { hash: 'xyz' })] });
  assert.strictEqual((await host.next('error')).code, 'etapas');
  host.send({ t: 'stages', stages: [trilha(1, { json: 'x'.repeat(200 * 1024) })] });
  assert.strictEqual((await host.next('error')).code, 'etapas');
  host.send({ t: 'stages', stages: Array.from({ length: 13 }, (_, i) => trilha(i)) });
  assert.strictEqual((await host.next('error')).code, 'etapas'); ok('pistas inválidas são recusadas');

  // visibilidade
  host.send({ t: 'visibility', pub: false }); await host.next('room');
  g2.send({ t: 'list' });
  assert.strictEqual((await g2.next('rooms')).rooms.length, 0); ok('sala privada some da lista');
  g2.send({ t: 'join', name: 'Caio', code });
  await g2.next('joined'); ok('sala privada entra por código');

  // sala cheia
  const extras = [];
  for (let i = 0; i < MAX_PLAYERS - 3; i++) {
    const x = await cliente(P); x.send({ t: 'join', name: 'P' + i, code }); await x.next('joined'); extras.push(x);
  }
  const lotado = await cliente(P); lotado.send({ t: 'join', name: 'Extra', code });
  assert.strictEqual((await lotado.next('error')).code, 'cheia'); ok('sala cheia recusa');

  // host sai: o próximo vira host
  g1.drain(); host.send({ t: 'leave' }); await host.next('left');
  r = await g1.next('room');
  assert.strictEqual(r.room.host, r.room.players[0].id); assert.strictEqual(r.room.players[0].name, 'Beto');
  assert.strictEqual(r.room.players.length, MAX_PLAYERS - 1); ok('host sai e a sala passa para o próximo');

  // todos saem: sala some
  for (const c of [g1, g2, ...extras, lotado]) c.close();
  await new Promise(r => setTimeout(r, 200));
  assert.strictEqual(srv.rooms.size, 0); ok('sala vazia é apagada');

  // mensagens estranhas não derrubam o servidor
  const x = await cliente(P);
  x.ws.send('isso não é json'); assert.strictEqual((await x.next('error')).code, 'json');
  x.send({ t: '__proto__' }); assert.strictEqual((await x.next('error')).code, 'tipo');
  x.send({ t: 'chat', text: 'oi' }); await x.quiet('chat');
  x.send({ t: 'stages', stages: 1 }); await x.quiet('error'); ok('mensagens estranhas são toleradas');

  // ---------- corrida: pronto, largar, relay ----------
  const H = await cliente(P), A = await cliente(P), B = await cliente(P);
  H.send({ t: 'create', name: 'Hugo', pub: false }); const room2 = (await H.next('joined')).room.code;
  H.send({ t: 'start', vmax: 14, ramp: .5, grip: 29, collide: true });
  assert.strictEqual((await H.next('error')).code, 'poucos'); ok('começar sozinho é recusado');
  A.send({ t: 'join', name: 'Ana', code: room2 }); const idA = (await A.next('joined')).you;
  B.send({ t: 'join', name: 'Beto', code: room2 }); const idB = (await B.next('joined')).you;
  H.send({ t: 'start', vmax: 14, ramp: .5, grip: 29, collide: true });
  assert.strictEqual((await H.next('error')).code, 'sem-etapas'); ok('começar sem pistas é recusado');
  H.send({ t: 'stages', stages: [trilha(1), trilha(2)] }); await H.next('room');
  H.send({ t: 'start', vmax: 14, ramp: .5, grip: 29, collide: true });
  let e = await H.next('error'); assert.strictEqual(e.code, 'nao-prontos'); assert(/Ana/.test(e.text) && /Beto/.test(e.text)); ok('começar sem todos prontos é recusado');
  A.send({ t: 'ready', ready: true }); B.send({ t: 'ready', ready: true });
  await new Promise(r => setTimeout(r, 150)); H.drain();
  H.send({ t: 'ready', ready: false }); await H.quiet('room');   // o host não alterna: está sempre pronto
  H.send({ t: 'stages', stages: [trilha(1), trilha(2), trilha(3)] });
  r = await H.next('room');
  assert.deepStrictEqual(r.room.players.map(p => p.ready), [true, false, false]); ok('mexer nas pistas zera o "pronto" dos visitantes (host sempre pronto)');
  A.send({ t: 'ready', ready: true }); B.send({ t: 'ready', ready: true });
  await new Promise(r => setTimeout(r, 150)); A.drain(); B.drain(); H.drain();
  A.send({ t: 'start', vmax: 14, ramp: .5, grip: 29, collide: true });
  assert.strictEqual((await A.next('error')).code, 'host'); ok('visitante não começa');
  H.send({ t: 'start', vmax: 99, ramp: .5, grip: 29, collide: true });
  assert.strictEqual((await H.next('error')).code, 'regras'); ok('regras fora dos limites são recusadas');
  H.send({ t: 'start', name: '  Copa  Teste ', vmax: 14, ramp: .5, grip: 29, collide: true });
  const st = [await H.next('started'), await A.next('started'), await B.next('started')];
  assert.deepStrictEqual(st[1], st[2]);
  assert.strictEqual(st[1].title, 'Copa Teste'); assert.deepStrictEqual(st[1].pilots.map(p => p.name), ['Hugo', 'Ana', 'Beto']);
  assert.strictEqual(st[1].stages.length, 3); assert.strictEqual(st[1].stages[0].json, '{"nome":"x"}'); assert.strictEqual(st[1].rules.vmax, 14);
  ok('todos prontos: o host começa e todos recebem pilotos, regras e o JSON das pistas');

  const C = await cliente(P); C.send({ t: 'join', name: 'Caio', code: room2 });
  assert.strictEqual((await C.next('error')).code, 'andamento');
  A.drain(); A.send({ t: 'color', color: COLORS[7] });
  assert.strictEqual((await A.next('error')).code, 'andamento'); ok('com o campeonato andando ninguém entra nem troca de cor');

  // entradas: visitante → só o host (com o id de quem mandou)
  H.drain(); A.drain(); B.drain();
  A.send({ t: 'in', held: true, re: 2 });
  const inp = await H.next('in');
  assert.deepStrictEqual([inp.from, inp.held, inp.re], [idA, true, 2]); await B.quiet('in'); await A.quiet('in'); ok('entrada do visitante vai só para o host');
  assert.strictEqual(inp.gp, 100); ok('sem nível do acelerador (cliente antigo) vale 100%');
  A.send({ t: 'in', held: true, gp: 55, re: 2 });
  assert.strictEqual((await H.next('in')).gp, 55);
  A.send({ t: 'in', held: true, gp: 900, re: 2 });
  assert.strictEqual((await H.next('in')).gp, 100); ok('nível do acelerador (gp) chega ao host, limitado a 1–100');
  H.send({ t: 'in', held: true }); await A.quiet('in'); ok('entrada do host não é repassada');
  for (let i = 0; i < 150; i++) A.send({ t: 'in', held: i % 2 === 0, re: 0 });
  await new Promise(r => setTimeout(r, 300));
  let n = 0; for (;;) { try { await H.next('in', 50); n++; } catch (e) { break; } }
  assert(n <= 121, 'entradas passaram do limite: ' + n); ok('entradas são limitadas por segundo (' + n + ')');

  // snapshots e eventos: só do host, para todos os outros
  H.drain(); A.drain(); B.drain();
  const snap = JSON.stringify({ t: 'snap', snap: { hi: 0, ph: 2, t: 1.5, cars: [{ s: 3 }, { s: 4 }] } });
  H.ws.send(snap);
  const s1 = await A.next('snap'), s2 = await B.next('snap'); assert.deepStrictEqual(s1, s2); assert.strictEqual(s1.snap.cars[1].s, 4); await H.quiet('snap');
  H.ws.send(JSON.stringify({ t: 'ev', k: 'res', hi: 0 })); assert.strictEqual((await A.next('ev')).k, 'res');
  A.ws.send(snap); B.ws.send(JSON.stringify({ t: 'ev', k: 'res' })); await H.quiet('snap'); await H.quiet('ev'); await B.quiet('snap');
  ok('snapshot e evento só valem se vierem do host');
  H.ws.send(JSON.stringify({ t: 'snap', pad: 'x'.repeat(20000) })); await A.quiet('snap'); ok('snapshot grande demais é descartado');

  // fim: só o host
  A.send({ t: 'end' }); assert.strictEqual((await A.next('error')).code, 'host');
  H.drain(); A.drain(); B.drain();
  H.send({ t: 'end', text: 'Fim de papo' });
  const l1 = await A.next('lobby'); await B.next('lobby'); await H.next('lobby');
  assert.strictEqual(l1.text, 'Fim de papo');
  r = await A.next('room'); assert.strictEqual(r.room.state, 'lobby'); assert.deepStrictEqual(r.room.players.map(p => p.ready), [true, false, false]);
  ok('o host encerra: todos voltam ao lobby e o "pronto" zera');

  // host sai no meio da corrida: encerra
  A.send({ t: 'ready', ready: true }); B.send({ t: 'ready', ready: true });
  await new Promise(r => setTimeout(r, 150));
  H.send({ t: 'start', vmax: 14, ramp: .5, grip: 29, collide: true }); await A.next('started');
  A.drain(); B.drain();
  H.send({ t: 'leave' });
  const l2 = await A.next('lobby'); assert(/host saiu/.test(l2.text)); ok('host que sai no meio da corrida encerra o campeonato');
  r = await A.next('room'); assert.strictEqual(r.room.host, idA);
  // visitante sai no meio: o campeonato segue
  B.send({ t: 'ready', ready: true }); await new Promise(r => setTimeout(r, 100));
  A.send({ t: 'start', vmax: 14, ramp: .5, grip: 29, collide: true }); await B.next('started');
  await new Promise(r => setTimeout(r, 150)); A.drain(); B.drain();
  B.send({ t: 'leave' }); r = await A.next('room'); assert.strictEqual(r.room.state, 'racing'); assert.strictEqual(r.room.players.length, 1); ok('visitante que sai no meio não derruba o campeonato');
  for (const c of [H, A, B, C]) c.close();

  // ---------- versão e limites por endereço ----------
  const v = await cliente(P, true);
  v.send({ t: 'create', name: 'Sem hello', pub: true });
  assert.strictEqual((await v.next('error')).code, 'versao');
  await new Promise(r => setTimeout(r, 100)); assert.strictEqual(v.ws.readyState, WebSocket.CLOSED); ok('mensagem antes do hello derruba a conexão');
  const v2 = await cliente(P, true);
  v2.send({ t: 'hello', ver: PROTOCOL + 1 });
  assert.strictEqual((await v2.next('error')).code, 'versao');
  await new Promise(r => setTimeout(r, 100)); assert.strictEqual(v2.ws.readyState, WebSocket.CLOSED); ok('versão de protocolo diferente é recusada e derrubada');

  const lim = await criarServidor({ port: 0, host: '127.0.0.1', maxConnPerIp: 3, maxCreatesPerMin: 2 });
  const L = [await cliente(lim.port), await cliente(lim.port), await cliente(lim.port)];
  const ex = new WebSocket(`ws://127.0.0.1:${lim.port}`); const got = [];
  ex.on('message', d => got.push(JSON.parse(d.toString())));
  await new Promise(r => ex.on('close', r));
  assert(got.some(m => m.code === 'muitas-conexoes')); ok('o 4º cliente do mesmo endereço é recusado');
  L[0].send({ t: 'create', name: 'A', pub: true }); await L[0].next('joined'); L[0].send({ t: 'leave' }); await L[0].next('left');
  L[0].send({ t: 'create', name: 'A', pub: true }); await L[0].next('joined'); L[0].send({ t: 'leave' }); await L[0].next('left');
  L[1].send({ t: 'create', name: 'B', pub: true });
  assert.strictEqual((await L[1].next('error')).code, 'muitas-salas'); ok('criar salas demais por minuto é recusado');
  L.forEach(c => c.close()); await lim.close();

  x.close(); host.close();

  await srv.close();
  console.log('\ntudo certo');
})().catch(e => { console.error('FALHOU:', e); process.exit(1); });
