// Gera as texturas PNG do construtor de autorama para a Unity.
// Uso: node gen-texturas.js <pasta-de-saida>
const fs = require('fs'), path = require('path'), zlib = require('zlib');
const OUT = process.argv[2];
fs.mkdirSync(OUT, { recursive: true });

/* ---------- PNG ---------- */
const CRC = new Uint32Array(256);
for (let n = 0; n < 256; n++){ let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; CRC[n] = c >>> 0; }
function crc32(buf){ let c = 0xffffffff; for (const b of buf) c = CRC[(c ^ b) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; }
function chunk(type, data){
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type), data]);
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(td));
  return Buffer.concat([len, td, crc]);
}
function writePNG(name, w, h, rgba){
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 6;
  const raw = Buffer.alloc((w * 4 + 1) * h);
  for (let y = 0; y < h; y++){ raw[y * (w * 4 + 1)] = 0; rgba.copy(raw, y * (w * 4 + 1) + 1, y * w * 4, (y + 1) * w * 4); }
  const png = Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
  fs.writeFileSync(path.join(OUT, name), png);
  console.log(name, w + 'x' + h);
}
// fn(x, y) recebe coordenadas contínuas de pixel e devolve [r, g, b, a] (a de 0 a 1).
function render(w, h, ss, fn){
  const out = Buffer.alloc(w * h * 4);
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++){
    let r = 0, g = 0, b = 0, a = 0;
    for (let j = 0; j < ss; j++) for (let i = 0; i < ss; i++){
      const c = fn(x + (i + .5) / ss, y + (j + .5) / ss);
      r += c[0] * c[3]; g += c[1] * c[3]; b += c[2] * c[3]; a += c[3];
    }
    const k = (y * w + x) * 4, n = ss * ss;
    out[k] = a ? Math.round(r / a) : 0; out[k + 1] = a ? Math.round(g / a) : 0; out[k + 2] = a ? Math.round(b / a) : 0;
    out[k + 3] = Math.round(a / n * 255);
  }
  return out;
}

/* ---------- Cores (as mesmas do HTML) ---------- */
const hex = s => [parseInt(s.slice(1, 3), 16), parseInt(s.slice(3, 5), 16), parseInt(s.slice(5, 7), 16)];
const C = {
  asphalt:hex('#2F3533'), edge:hex('#F2F2EC'), rail:hex('#AEB4B8'), slot:hex('#0E1110'), curb:hex('#D13A2E'),
  laneA:hex('#D3261C'), laneB:hex('#1F5ED4'), accent:hex('#E9A800'), warn:hex('#C2410C'),
  board:hex('#F3F6F0'), minor:hex('#D5DCD2'), major:hex('#A6B3A4'),
  laneAdark:hex('#8E1912'), laneBdark:hex('#163F8F'), tire:hex('#141716'), glass:hex('#1B2730'), glassHi:hex('#5C7383'),
};
const mix = (a, b, t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
const shade = (c, d) => c.map(v => Math.max(0, Math.min(255, v + d)));
const over = (dst, col, alpha) => {           // compõe col (alpha) sobre dst [r,g,b,a]
  const a = alpha + dst[3] * (1 - alpha);
  if (a <= 0) return [0, 0, 0, 0];
  return [0, 1, 2].map(i => (col[i] * alpha + dst[i] * dst[3] * (1 - alpha)) / a).concat(a);
};

/* ---------- Ruído contínuo e repetível ---------- */
function rng(seed){ return () => { seed |= 0; seed = seed + 0x6D2B79F5 | 0; let t = Math.imul(seed ^ seed >>> 15, 1 | seed); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; }; }
function valueNoise(period, seed){
  const r = rng(seed), g = Float32Array.from({ length: period * period }, r);
  const at = (i, j) => g[(((j % period) + period) % period) * period + (((i % period) + period) % period)];
  const s = t => t * t * (3 - 2 * t);
  return (u, v) => {                          // u, v de 0 a 1 (repete nas bordas)
    const x = u * period, y = v * period, xi = Math.floor(x), yi = Math.floor(y), fx = s(x - xi), fy = s(y - yi);
    const a = at(xi, yi) + (at(xi + 1, yi) - at(xi, yi)) * fx, b = at(xi, yi + 1) + (at(xi + 1, yi + 1) - at(xi, yi + 1)) * fx;
    return a + (b - a) * fy;
  };
}
const octaves = [8, 16, 32, 64, 128].map((p, i) => [valueNoise(p, 11 + i), .5 ** i]);
const fbm = (u, v) => { let s = 0, n = 0; for (const [f, w] of octaves){ s += f(u, v) * w; n += w; } return s / n; };
const grain = (() => { const r = rng(99), g = Float32Array.from({ length: 512 * 512 }, r); return (u, v) => g[(Math.floor(((v % 1) + 1) % 1 * 512)) * 512 + Math.floor(((u % 1) + 1) % 1 * 512)]; })();
function asphaltAt(u, v){
  let c = shade(C.asphalt, (fbm(u, v) - .5) * 22);
  const gr = grain(u, v);
  if (gr > .975) c = shade(c, 26); else if (gr < .02) c = shade(c, -14);
  return c;
}

/* ---------- 1. Asfalto (repete nos dois sentidos) ---------- */
writePNG('asfalto.png', 512, 512, render(512, 512, 1, (x, y) => [...asphaltAt(x / 512, y / 512), 1]));

/* ---------- 2. Faixa completa da pista: u atravessa a pista (esquerda→direita), v = 1 célula ---------- */
// curb: 0 sem zebra, -1 lado de fora à esquerda, +1 lado de fora à direita.
// banked: curva inclinada, que clareia rumo ao lado de fora (o mais alto) e tem muro em vez de zebra.
function trackSection(curb, banked = false){
  const N = 512, LANES = [[-.25, C.laneA], [.25, C.laneB]];
  return render(N, N, 3, (x, y) => {
    const u = x / N, v = 1 - y / N, o = u - .5;           // v cresce para cima na textura da Unity
    let c = asphaltAt(u, v);
    if (banked) c = mix(c, C.edge, Math.floor((curb * o + .5) * 4) / 4 * .26);
    for (const [l, col] of LANES) if (Math.abs(o - l) < .21) c = mix(c, col, .11);
    const ao = Math.abs(o);
    if (banked && ao < .46 && (v * 4) % 1 < .36) c = mix(c, C.accent, .72);   // listras amarelas: 4 por célula
    const curbHere = curb !== 0 && Math.sign(o) === curb;
    if (banked && curbHere && ao > .44) c = shade(C.rail, (fbm(u * 3, v) - .5) * 16);
    else if (!banked && curbHere && ao > .40 && ao < .48) c = Math.floor(v * 4) % 2 ? C.edge : C.curb;
    else if (ao > .43 && ao < .47) c = C.edge;
    for (const [l] of LANES){
      const d = Math.abs(o - l);
      if (d > .05 && d < .08) c = shade(C.rail, (fbm(u * 3, v) - .5) * 20);
      if (d < .022) c = C.slot;
    }
    return [...c, 1];
  });
}
writePNG('pista-reta.png', 512, 512, trackSection(0));
writePNG('pista-curva-direita.png', 512, 512, trackSection(-1));   // curva à direita: lado de fora é a esquerda
writePNG('pista-curva-esquerda.png', 512, 512, trackSection(1));
writePNG('pista-curva-inclinada-direita.png', 512, 512, trackSection(-1, true));
writePNG('pista-curva-inclinada-esquerda.png', 512, 512, trackSection(1, true));

// Cruzamento: a peça inteira numa imagem (u atravessa a pista, v vai do começo ao fim das células da peça).
// Cada fenda faz um S de um lado para o outro (deslocamento ±0,25·cos(π·v)) e as duas se cruzam no meio.
function crossSection(cells){
  const W = 256, H = 256 * cells;
  return render(W, H, 3, (x, y) => {
    const u = x / W, v = 1 - y / H, o = u - .5;
    let c = asphaltAt(u, v * cells);
    const ao = Math.abs(o);
    if (ao > .43 && ao < .47) c = C.edge;
    const lanes = [-1, 1].map(s => s * .25 * Math.cos(Math.PI * v));
    // Faixa amarela fininha no meio, marcando o X.
    if (Math.abs(v - .5) < .024 / cells && ao < .4) c = mix(c, C.accent, .8);
    for (const l of lanes){ const d = Math.abs(o - l); if (d > .05 && d < .08) c = shade(C.rail, (fbm(u * 3, v * cells) - .5) * 20); }
    for (const l of lanes){ if (Math.abs(o - l) < .022) c = C.slot; }
    return [...c, 1];
  });
}
writePNG('pista-cruzamento.png', 256, 1024, crossSection(4));
writePNG('pista-cruzamento-curto.png', 256, 512, crossSection(2));

/* ---------- 3. Zebra avulsa (4 listras por célula) ---------- */
writePNG('zebra.png', 64, 256, render(64, 256, 1, (x, y) => [...(Math.floor(y / 64) % 2 ? C.edge : C.curb), 1]));

/* ---------- 4. Bandeirada: 8 × 2 quadrados (1 célula × 0,25 célula) ---------- */
writePNG('bandeirada.png', 512, 128, render(512, 128, 1, (x, y) => [...((Math.floor(x / 64) + Math.floor(y / 64)) % 2 ? C.slot : C.edge), 1]));

/* ---------- 5. Grade da mesa: 4 × 4 células, linha forte na borda do bloco ---------- */
writePNG('grade-mesa.png', 256, 256, render(256, 256, 1, (x, y) => {
  x = Math.floor(x); y = Math.floor(y);
  const major = x < 2 || x > 253 || y < 2 || y > 253;
  const minor = x % 64 === 0 || x % 64 === 63 || y % 64 === 0 || y % 64 === 63;
  return [...(major ? C.major : minor ? C.minor : C.board), 1];
}));

/* ---------- 6. Listras de sobreposição (transparente, repete) ---------- */
writePNG('sobreposicao.png', 128, 128, render(128, 128, 3, (x, y) => {
  const s = ((x + y) % 64 + 64) % 64;
  return s < 22 ? [...C.warn, .85] : [...C.warn, .22];
}));

/* ---------- 7. Seta do ponto de inserção (aponta para +X) ---------- */
function segDist(px, py, ax, ay, bx, by){
  const dx = bx - ax, dy = by - ay, t = Math.max(0, Math.min(1, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)));
  return Math.hypot(px - ax - dx * t, py - ay - dy * t);
}
writePNG('seta-insercao.png', 256, 256, render(256, 256, 4, (x, y) => {
  const S = 200, wx = (x - 128) / S + .25, wy = (y - 128) / S;   // 1 célula = 200 px
  const d = Math.min(segDist(wx, wy, 0, -.6, 0, .6), segDist(wx, wy, .2, -.28, .48, 0), segDist(wx, wy, .48, 0, .2, .28));
  return d < .045 ? [...C.accent, 1] : [0, 0, 0, 0];
}));

/* ---------- 8. Carrinhos vistos de cima (frente para +X), 1 célula = 1000 px ---------- */
// Carroceria de 0,60 × 0,32 célula, a mesma caixa de colisão do HTML. A imagem cobre 0,64 × 0,384.
function rbox(px, py, cx, cy, hx, hy, r){
  const qx = Math.abs(px - cx) - hx + r, qy = Math.abs(py - cy) - hy + r;
  return Math.hypot(Math.max(qx, 0), Math.max(qy, 0)) + Math.min(Math.max(qx, qy), 0) - r;
}
function car(body, dark){
  return render(640, 384, 4, (x, y) => {
    const px = (x - 320) / 1000, py = (y - 192) / 1000;
    let c = [0, 0, 0, 0];
    const sh = rbox(px - .01, py - .012, 0, 0, .29, .15, .07);                  // sombra suave
    if (sh < .022) c = over(c, [0, 0, 0], .28 * Math.min(1, (.022 - sh) / .022));
    for (const wx of [-.19, .19]) for (const wy of [-.148, .148])
      if (rbox(px, py, wx, wy, .052, .03, .01) < 0) c = over(c, C.tire, 1);
    if (rbox(px, py, 0, 0, .285, .14, .07) < 0){
      c = over(c, body, 1);
      if (Math.abs(py) < .022 && px > .14) c = over(c, C.edge, 1);                // faixa no capô
      if (rbox(px, py, .09, 0, .058, .095, .035) < 0)                            // para-brisa
        c = over(c, Math.abs((px - .1) - (py + .03) * .6) < .009 ? C.glassHi : C.glass, 1);
      if (Math.hypot(px + .08, py) < .058) c = over(c, C.edge, 1);               // círculo do número
    }
    if (rbox(px, py, -.272, 0, .026, .158, .008) < 0) c = over(c, dark, 1);      // aerofólio
    return c;
  });
}
writePNG('carrinho-vermelho.png', 640, 384, car(C.laneA, C.laneAdark));
writePNG('carrinho-azul.png', 640, 384, car(C.laneB, C.laneBdark));

/* ---------- 9. Ícones das peças (mesma escala entre si, pista apontando para cima) ---------- */
const PIECES = {
  'base-largada':        { kind:'s', len:2, start:true },
  'reta-curta':          { kind:'s', len:2 },
  'reta-comprida':       { kind:'s', len:4 },
  'reta-curtinha':       { kind:'s', len:1 },
  'curva-longa-esquerda':{ kind:'c', angle:90, dir:-1 },
  'curva-longa-direita': { kind:'c', angle:90, dir:1 },
  'meia-curva-esquerda': { kind:'c', angle:45, dir:-1 },
  'meia-curva-direita':  { kind:'c', angle:45, dir:1 },
  'curva-longa-inclinada-esquerda': { kind:'c', angle:90, dir:-1, bank:true },
  'curva-longa-inclinada-direita':  { kind:'c', angle:90, dir:1, bank:true },
  'curva-curta-inclinada-esquerda': { kind:'c', angle:45, dir:-1, bank:true },
  'curva-curta-inclinada-direita':  { kind:'c', angle:45, dir:1, bank:true },
  'rampa-sobe':          { kind:'s', len:4, rise:1 },
  'rampa-desce':         { kind:'s', len:4, rise:-1 },
  'cruzamento':          { kind:'s', len:4, cross:true },
  'cruzamento-curto':    { kind:'s', len:2, cross:true },
};
// Distância de um ponto até um segmento, em coordenadas locais da peça (s ao longo, o de lado).
function segDistLocal(s, o, s1, o1, s2, o2){
  const ds = s2 - s1, dO = o2 - o1, t = Math.max(0, Math.min(1, ((s - s1) * ds + (o - o1) * dO) / (ds * ds + dO * dO)));
  return Math.hypot(s - s1 - ds * t, o - o1 - dO * t);
}
// Setas da rampa apontando para o lado alto.
function onChevron(L, rise, len){
  for (const sc of [len * .25, len * .5, len * .75]){
    const tip = sc + .26 * rise, back = sc - .06 * rise;
    if (segDistLocal(L.s, L.o, back, -.29, tip, 0) < .06 || segDistLocal(L.s, L.o, tip, 0, back, .29) < .06) return true;
  }
  return false;
}
const R = 2.5, rad = d => d * Math.PI / 180;
// Coordenadas locais da imagem (y para baixo). A peça começa em (0,0) subindo.
// Devolve { s: distância ao longo, o: deslocamento à direita } ou null.
function localOf(p, X, Y){
  if (p.kind === 's'){ const s = -Y; return s >= 0 && s <= p.len ? { s, o:X } : null; }
  const cx = p.dir * R, rho = Math.hypot(X - cx, Y);
  let phi = Math.atan2(Y, X - cx) * 180 / Math.PI;           // ângulo a partir do centro
  let t;
  if (p.dir > 0){ phi = (phi + 360) % 360; t = (phi - 180) / p.angle; }  // de 180° subindo para 270°
  else { t = -phi / p.angle; }                                          // de 0° descendo para -ângulo
  if (t < 0 || t > 1) return null;
  return { s:t * rad(p.angle) * R, o:p.dir * (R - rho) };
}
function bbox(p){
  const pts = [];
  if (p.kind === 's') pts.push([-.5, 0], [.5, 0], [-.5, -p.len], [.5, -p.len]);
  else for (let k = 0; k <= 32; k++){
    const a = rad(p.dir > 0 ? 180 + p.angle * k / 32 : -p.angle * k / 32);
    for (const rr of [R - .5, R + .5]) pts.push([p.dir * R + Math.cos(a) * rr, Math.sin(a) * rr]);
  }
  const xs = pts.map(q => q[0]), ys = pts.map(q => q[1]);
  return [(Math.min(...xs) + Math.max(...xs)) / 2, (Math.min(...ys) + Math.max(...ys)) / 2];
}
for (const [name, p] of Object.entries(PIECES)){
  const [mx, my] = bbox(p), BOX = 4.4, N = 256;
  writePNG('icone-' + name + '.png', N, N, render(N, N, 4, (x, y) => {
    const X = mx + (x / N - .5) * BOX, Y = my + (y / N - .5) * BOX;
    const L = localOf(p, X, Y);
    if (!L || Math.abs(L.o) > .5) return [0, 0, 0, 0];
    let c = C.asphalt;
    if (p.bank) c = mix(c, C.edge, Math.floor((.5 - p.dir * L.o) * 4) / 4 * .28);   // clareia rumo ao lado de fora
    if (p.bank && Math.abs(L.o) < .46 && L.s % .3 < .11) c = C.accent;                // listras amarelas transversais
    // No cruzamento, os trilhos trocam de lado em S; nas outras peças ficam em ±0,25.
    const la = p.cross ? -.25 * Math.cos(Math.PI * L.s / p.len) : -.25;
    if (Math.abs(L.o + la) < .045) c = C.laneB;
    if (Math.abs(L.o - la) < .045) c = C.laneA;
    if (p.start && Math.abs(L.s - 1) < .11) c = (Math.floor((L.o + .5) / .125) + Math.floor((L.s - .89) / .11)) % 2 ? C.slot : C.edge;
    if (p.rise && onChevron(L, p.rise, p.len)) c = C.accent;
    return [...c, 1];
  }));
}
