// Roda o construtor em HTML no Node, sem navegador, e executa um teste dentro dele (com acesso a todas as funções da página).
// Uso: node ferramentas/teste-html.js <teste.js>   (o teste é JS solto, colado no fim do script da página)
// Exemplo de teste: setMode('race'); pickRaceTrack('ex:oval'); startRace(); race.paths = buildPaths();
//                   for (let t = 0; t < 30; t += 1/60) step(1/60); console.log(race.cars[0].laps);
// O DOM é falso (um Proxy que aceita tudo), então desenho e eventos não são testados aqui: use o navegador para isso.
const fs = require('fs'), path = require('path');
const page = path.join(__dirname, '..', 'html', 'index.html');
const test = fs.readFileSync(process.argv[2], 'utf8');
let src = fs.readFileSync(page, 'utf8').match(/<script>([\s\S]*)<\/script>/)[1];

const mk = (extra = {}) => new Proxy(function(){}, {
  get(t, p){
    if (p in extra) return extra[p];
    if (p === Symbol.toPrimitive) return () => '';
    if (p === Symbol.iterator) return function* (){};
    if (p === 'clientWidth') return 900;
    if (p === 'clientHeight') return 600;
    if (p === 'value' || p === 'textContent' || p === 'innerHTML') return '';
    return S;
  },
  set(){ return true; }, apply(){ return S; }, construct(){ return S; },
});
const S = mk();
const pad = () => mk({ querySelectorAll: () => ['who', 'state', 'gas', 'fill', 'speed', 'slip', 'reset'].map(r => mk({ dataset:{ r } })) });
global.document = { querySelector: () => S, querySelectorAll: s => s === '.pad' ? [pad(), pad()] : [], activeElement:null, body:S, documentElement:S };
global.window = { addEventListener(){} };
const store = {};
global.localStorage = { getItem: k => k in store ? store[k] : null, setItem: (k, v) => { store[k] = String(v); } };
global.ResizeObserver = class { observe(){} };
global.requestAnimationFrame = () => 1;
global.performance = { now: () => 0 };
global.navigator = {};
global.matchMedia = () => ({ matches:false, addEventListener(){}, addListener(){} });

src = src.replace(/\}\)\(\);\s*$/, test + '\n})();');
eval(src);
