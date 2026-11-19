// Tabelas de idioma (seção 88 do guia). Junta os textos de L.T("…"), L.F("…") e L.N("…") dos scripts e os nomes e falas do
// carreira.json, e compara com Assets/Autorama/Resources/Idiomas/<código>.txt (português TAB tradução).
//   node ferramentas/idiomas.js              quantos faltam e quantos sobram em cada tabela
//   node ferramentas/idiomas.js faltam en    os textos sem tradução em inglês, um por linha (com \n escrito)
//   node ferramentas/idiomas.js limpa        tira das tabelas as linhas que o jogo não usa mais
const fs = require('fs');
const path = require('path');

const raiz = path.join(__dirname, '..', 'Assets', 'Autorama');
const pasta = path.join(raiz, 'Resources', 'Idiomas');
const idiomas = ['en', 'es', 'fr', 'de', 'ja'];

function scripts(dir, out = []) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) { if (e.name !== 'Editor') scripts(p, out); }
    else if (e.name.endsWith('.cs')) out.push(p);
  }
  return out;
}

// Literal C# comum logo depois de L.T( ou L.F(; devolve o texto já sem os escapes.
function chaves() {
  const set = new Set();
  const re = /\bL\.[TFN]\(\s*"((?:[^"\\]|\\.)*)"/g;
  for (const f of scripts(path.join(raiz, 'Scripts'))) {
    const src = fs.readFileSync(f, 'utf8');
    for (const m of src.matchAll(re)) set.add(JSON.parse('"' + m[1].replace(/\\'/g, "'") + '"'));
  }
  // Avisos fixos do servidor Node: o jogo traduz o texto que chega (L.T(m.text)).
  const srv = fs.readFileSync(path.join(__dirname, '..', 'servidor', 'servidor.js'), 'utf8');
  for (const m of srv.matchAll(/\b(?:fail\(c, '[^']*'|say\(r)\s*,\s*'((?:[^'\\]|\\.)*)'/g)) set.add(m[1].replace(/\\'/g, "'"));
  const car = JSON.parse(fs.readFileSync(path.join(raiz, 'Resources', 'Carreira', 'carreira.json'), 'utf8'));
  (function anda(o) {
    if (Array.isArray(o)) return o.forEach(anda);
    if (o && typeof o === 'object')
      for (const [k, v] of Object.entries(o)) {
        if (typeof v === 'string' && ['nome', 'rivalFrase', 'rivalDerrota', 'rivalVitoria'].includes(k) && v) set.add(v);
        else anda(v);
      }
  })(car);
  return set;
}

const esc = s => s.replace(/\t/g, '\\t').replace(/\n/g, '\\n');
const des = s => s.replace(/\\n/g, '\n').replace(/\\t/g, '\t');

function tabela(cod) {
  const p = path.join(pasta, cod + '.txt');
  const m = new Map();
  if (!fs.existsSync(p)) return m;
  for (const linha of fs.readFileSync(p, 'utf8').split('\n')) {
    const t = linha.indexOf('\t');
    if (t > 0) m.set(des(linha.slice(0, t)), des(linha.slice(t + 1).replace(/\r$/, '')));
  }
  return m;
}

const todas = chaves();
const [cmd, cod] = process.argv.slice(2);
if (cmd === 'faltam') {
  const t = tabela(cod);
  for (const k of todas) if (!t.get(k)) console.log(esc(k));
} else if (cmd === 'junta') {
  // node ferramentas/idiomas.js junta en lote.txt: a linha i do lote é a tradução do i-ésimo texto que falta.
  const t = tabela(cod);
  const falta = [...todas].filter(k => !t.get(k));
  const lote = fs.readFileSync(process.argv[4], 'utf8').split('\n').map(l => l.replace(/\r$/, '')).filter(l => l !== '');
  if (lote.length > falta.length) { console.log(`o lote tem ${lote.length} linhas e faltam ${falta.length}`); process.exit(1); }
  const p = path.join(pasta, cod + '.txt');
  const novas = lote.map((tr, i) => esc(falta[i]) + '\t' + tr);
  fs.appendFileSync(p, (fs.existsSync(p) && fs.readFileSync(p, 'utf8').length && !fs.readFileSync(p, 'utf8').endsWith('\n') ? '\n' : '') + novas.join('\n') + '\n');
  console.log(`${cod}: +${lote.length}`);
} else if (cmd === 'limpa') {
  for (const c of idiomas) {
    const t = tabela(c);
    const linhas = [...t].filter(([k]) => todas.has(k)).map(([k, v]) => esc(k) + '\t' + esc(v));
    fs.writeFileSync(path.join(pasta, c + '.txt'), linhas.join('\n') + '\n');
  }
} else {
  console.log(`${todas.size} textos no jogo`);
  for (const c of idiomas) {
    const t = tabela(c);
    let falta = 0, sobra = 0;
    for (const k of todas) if (!t.get(k)) falta++;
    for (const k of t.keys()) if (!todas.has(k)) sobra++;
    console.log(`${c}: faltam ${falta}, sobram ${sobra}`);
    // a tradução precisa ter os mesmos {0}, {1}… (o formato depois do : pode mudar) e as mesmas tags <b>, <color>, <size>
    const marcas = s => [...s.matchAll(/\{\d+|<\/?(?:b|i|color|size)\b/g)].map(m => m[0]).sort().join(' ');
    for (const [k, v] of t) if (todas.has(k) && v && marcas(k) !== marcas(v)) console.log(`  ${c} marcas diferentes: ${esc(k).slice(0, 70)}  →  ${esc(v).slice(0, 70)}`);
  }
}
