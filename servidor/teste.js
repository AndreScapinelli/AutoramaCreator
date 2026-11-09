// Teste do servidor de salas: node teste.js
'use strict';
const assert = require('assert');
const WebSocket = require('ws');
const { criarServidor, COLORS, MAX_PLAYERS } = require('./servidor');

function cliente(port) {
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
  return c.open().then(() => c.next('hello')).then(() => c);
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

  x.close(); host.close();
  await srv.close();
  console.log('\ntudo certo');
})().catch(e => { console.error('FALHOU:', e); process.exit(1); });
