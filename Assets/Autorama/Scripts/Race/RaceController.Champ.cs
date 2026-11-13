using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static Autorama.BuilderPanel;

namespace Autorama
{
    // A aba Campeonato: pilotos (gente ou IA), etapas, baterias em rodízio de trilhos, tabela geral e recordes.
    // As regras são as do construtor em HTML (seção 19 do guia); as contas ficam em Champ (ChampData.cs).
    public partial class RaceController
    {
        ChampStore store;
        List<Pilot> roster;
        RecordsFile recs;

        VisualElement cSetup, cRun, cRoster, cColors, cSkillBox, cStages, cPick, cHistBlock, cHist;
        VisualElement cHeat, cActs, cLive, cScore, cStageBlock, cStageTable, cTable, cRecs, cStageList;
        Label cPilotCount, cFormTitle, cSkillHint, cRules, cPlan, cStageCount, cTitle, cProgress, cHint, cClock, cStageTitle, cPts;
        TextField cName, pName, pTag;
        Toggle pAi;
        SliderInt pSkill;
        Button pSave, pCancel, cStart, cQuit, cKind2, cKind4, cKind6, cKind8, cByLaps, cByTime;
        string editPilot, pColor;
        bool tagTouched;
        readonly Dictionary<string, Texture2D> carTex = new Dictionary<string, Texture2D>();
        static readonly Color[] LaneColors = { new Color32(240, 74, 63, 255), new Color32(80, 137, 242, 255), new Color32(56, 186, 98, 255), new Color32(242, 183, 40, 255), new Color32(139, 63, 217, 255), new Color32(25, 181, 201, 255), new Color32(240, 120, 30, 255), new Color32(224, 72, 150, 255) };

        // ---------- Montagem ----------

        void BuildChampUI()
        {
            store = ChampFiles.LoadStore();
            roster = ChampFiles.LoadPilots();
            recs = ChampFiles.LoadRecords();
            var box = panel.ChampBox;

            // Montar o campeonato
            cSetup = El(null, box);
            var head = Section(cSetup, "NOVO CAMPEONATO");
            Text("Escolha os pilotos e as pistas. Em cada etapa, todo piloto corre uma bateria em cada trilho, em rodízio: ninguém leva vantagem por ficar sempre do lado de dentro.", "hint", head);
            cName = Field("Nome do campeonato", 40, head);
            cName.RegisterValueChangedCallback(e => { store.draft.name = e.newValue; SaveStore(); });
            // Tipo: 2 ou 4 carros por bateria. As etapas são todas pistas desse tipo (nunca se misturam).
            Text("TAMANHO DA PISTA", "h3", head);
            var kind = El("segmented", head);
            cKind2 = Btn("2 trilhos", () => ChampKind(2), "btn", kind);
            cKind4 = Btn("4 trilhos", () => ChampKind(4), "btn", kind);
            cKind6 = Btn("6 trilhos", () => ChampKind(6), "btn", kind);
            cKind8 = Btn("8 trilhos", () => ChampKind(8), "btn", kind);
            cKind2.tooltip = cKind4.tooltip = cKind6.tooltip = cKind8.tooltip = "Todas as etapas correm nesse tamanho (qualquer pista é adaptada). Depois de começar, o tamanho não muda. Com menos pilotos que trilhos, sobra trilho vazio.";
            // Formato: por voltas (rodízio de baterias) ou por tempo (cada bateria passa por todos os trilhos em manches).
            Text("FORMATO", "h3", head);
            var fmt = El("segmented", head);
            cByLaps = Btn("Por voltas", () => { store.draft.byTime = false; SaveStore(); DrawChampSide(); }, "btn", fmt);
            cByTime = Btn("Por tempo", () => { store.draft.byTime = true; SaveStore(); DrawChampSide(); }, "btn", fmt);
            cByTime.tooltip = "Cada bateria tem uma duração e se divide em uma manche por trilho. Vence quem andar mais.";

            var pil = Section(cSetup, "PILOTOS");
            cPilotCount = Text("", "small", pil);
            Text("Marque quem vai correr: de 2 a 8. A ordem em que você marca é a ordem do rodízio. Piloto IA é dirigido pelo computador e não bate recorde da pista.", "hint", pil);
            cRoster = El(null, pil);
            var form = El("pform", pil);
            cFormTitle = Text("NOVO PILOTO", "h3", form);
            var names = El("pf-row", form);
            pName = Field("Nome", 20, names); pName.AddToClassList("pf-name");
            pTag = Field("Sigla", 3, names); pTag.AddToClassList("pf-tag");
            pName.RegisterValueChangedCallback(e => { if (!tagTouched) pTag.SetValueWithoutNotify(Champ.TagOf(e.newValue)); });
            pTag.RegisterValueChangedCallback(e => { tagTouched = true; pTag.SetValueWithoutNotify(Champ.TagOf(e.newValue)); });
            foreach (var f in new[] { pName, pTag })
                f.RegisterCallback<KeyDownEvent>(ev => { if (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter) { SavePilot(); f.Blur(); } }, TrickleDown.TrickleDown);
            cColors = El("swatches", form);
            pAi = new Toggle("Piloto IA (o computador dirige)");
            pAi.AddToClassList("setting"); pAi.AddToClassList("ai-toggle");
            form.Add(pAi);
            cSkillBox = El(null, form);
            pSkill = new SliderInt(1, 100) { value = 50 };
            pSkill.AddToClassList("setting");
            cSkillBox.Add(pSkill);
            cSkillHint = Text("", "small", cSkillBox);
            pAi.RegisterValueChangedCallback(e => SetAiForm(e.newValue, pSkill.value));
            pSkill.RegisterValueChangedCallback(e => SetAiForm(true, e.newValue));
            var formRow = El("row", form);
            formRow.style.marginTop = 8;
            pSave = Btn("Adicionar piloto", SavePilot, "btn btn--primary", formRow);
            pCancel = Btn("Cancelar", () => { ResetPilotForm(); DrawChampSide(); }, "btn", formRow);
            SetAiForm(false, 50);

            var stg = Section(cSetup, "ETAPAS");
            cStageCount = Text("", "small", stg);
            Text("Cada etapa é uma pista com um número de voltas. Clique numa pista abaixo para incluir.", "hint", stg);
            cStages = El(null, Caixa(stg));
            Text("PISTAS DISPONÍVEIS", "h3", stg);
            cPick = El(null, Caixa(stg));

            var rules = Section(cSetup, "REGRAS");
            cRules = Text("", "small", rules);
            cPlan = Text("", "body plan", rules);
            cStart = Btn("Começar campeonato", ChampBegin, "btn btn--primary", rules);

            cHistBlock = Section(cSetup, "CAMPEONATOS ANTERIORES");
            cHist = El(null, cHistBlock);

            // Durante o campeonato
            cRun = El(null, box);
            var run = El("section", cRun);
            cTitle = Text("", "h2", run);
            cProgress = Text("", "hint", run);
            cHeat = El(null, run);
            cActs = El("row", run);
            cHint = Text("", "hint", run);
            cLive = El(null, run);
            var clockRow = El("row", cLive);
            Text("Tempo de prova", "stats-key", clockRow).style.marginRight = 8;
            cClock = Text("0,00 s", "clock", clockRow);
            cScore = El("score", cLive);

            cStageBlock = El("section", cRun);
            cStageTitle = Text("", "h2", cStageBlock);
            cStageTable = El("stand", cStageBlock);

            var table = Section(cRun, "TABELA GERAL");
            cTable = El("stand", table);
            cPts = Text("", "small", table);
            cPts.style.marginTop = 6;

            cRecs = El(null, Section(cRun, "RECORDES DA PISTA"));

            var stages = Section(cRun, "ETAPAS");
            cStageList = El(null, Caixa(stages));
            cQuit = Btn("Abandonar campeonato", null, "btn btn--danger", stages);
            cQuit.style.marginTop = 8;
            cQuit.clicked += () =>
            {
                if (!panel.ConfirmTwice("abandonar")) { cQuit.text = "Abandonar? Clique de novo"; cQuit.schedule.Execute(() => cQuit.text = "Abandonar campeonato").ExecuteLater(3000); return; }
                cQuit.text = "Abandonar campeonato";
                store.hasActive = false; store.active = null; SaveStore();
                RefreshChamp();
            };
        }

        // Troca o tipo do campeonato. As etapas do outro tipo saem (pede um segundo clique se houver alguma).
        void ChampKind(int faixas)
        {
            var D = store.draft;
            if (D.faixas == faixas) return;
            // O tamanho vale para o campeonato inteiro e é livre até começar: as etapas já escolhidas são adaptadas.
            D.faixas = TrackMath.Faixas(faixas);
            FitDraftStages();
            SaveStore();
            RefreshChamp();
        }

        // Deixa todas as etapas do rascunho no tamanho do campeonato (a identidade da pista muda com o tamanho).
        void FitDraftStages()
        {
            var D = store.draft;
            D.stages.RemoveAll(s => s.data == null);
            foreach (var s in D.stages)
                if (s.data.Faixas != D.faixas) { s.data.faixas = D.faixas; s.hash = Champ.TrackHash(s.data); }
        }

        static TextField Field(string placeholder, int max, VisualElement parent)
        {
            var f = new TextField { maxLength = max };
            f.AddToClassList("name-field");
            f.textEdition.placeholder = placeholder;
            f.textEdition.hidePlaceholderOnFocus = true;
            parent.Add(f);
            return f;
        }

        void SaveStore() { if (!ChampFiles.SaveStore(store)) panel.Toast("Não deu para salvar o campeonato no disco."); }

        // ---------- Pilotos ----------

        List<Pilot> DraftPilots()
        {
            var list = new List<Pilot>();
            foreach (var id in store.draft.pilots) { var p = roster.Find(x => x.id == id); if (p != null) list.Add(p); }
            return list;
        }

        string FreeColor()
        {
            var used = new HashSet<string>();
            foreach (var p in DraftPilots()) if (p.id != editPilot) used.Add(p.color);
            foreach (var c in Champ.Colors) if (!used.Contains(c)) return c;
            return Champ.Colors[0];
        }

        void SetAiForm(bool on, int skill)
        {
            pAi.SetValueWithoutNotify(on);
            pSkill.SetValueWithoutNotify(skill);
            pSkill.label = $"Qualidade: {skill}";
            cSkillHint.text = Champ.SkillWord(skill);
            cSkillBox.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void ResetPilotForm()
        {
            editPilot = null; pColor = null; tagTouched = false;
            pName.SetValueWithoutNotify(""); pTag.SetValueWithoutNotify("");
            SetAiForm(false, 50);
        }

        void SavePilot()
        {
            string name = pName.value.Trim();
            int robots = roster.FindAll(p => p.ai).Count;
            if (name == "" && pAi.value && editPilot == null) name = $"Robô {robots + 1}";     // piloto IA sem nome ganha um
            if (name == "") { pName.Focus(); return; }
            if (name.Length > 20) name = name.Substring(0, 20);
            string tag = Champ.TagOf(pTag.value);
            if (tag == "") tag = Champ.TagOf(name);
            if (tag == "") tag = "P";
            string color = pColor ?? FreeColor();
            if (editPilot != null)
            {
                var p = roster.Find(x => x.id == editPilot);
                if (p != null) { p.name = name; p.tag = tag; p.color = color; p.ai = pAi.value; p.skill = pSkill.value; }
            }
            else
            {
                var p = new Pilot { id = Champ.NewId("j"), name = name, tag = tag, color = color, ai = pAi.value, skill = pSkill.value };
                roster.Add(p);
                if (store.draft.pilots.Count < Champ.MaxPilots) { store.draft.pilots.Add(p.id); SaveStore(); }   // já vem marcado
            }
            ChampFiles.SavePilots(roster);
            ResetPilotForm();
            DrawChampSide();
        }

        string ChampProblem()
        {
            var ps = DraftPilots();
            int F = store.draft.faixas;
            if (ps.Count < Champ.MinPilots(F, store.draft.byTime)) return "Marque pelo menos 2 pilotos.";
            if (ps.Count > Champ.MaxPilots) return $"No máximo {Champ.MaxPilots} pilotos.";
            for (int i = 0; i < ps.Count; i++)
                for (int j = 0; j < i; j++)
                    if (ps[i].color == ps[j].color) return $"{ps[j].name} e {ps[i].name} estão com a mesma cor. Troque a cor de um deles.";
            if (store.draft.stages.Count == 0) return "Inclua pelo menos uma pista nas etapas.";
            FitDraftStages();
            return "";
        }

        // ---------- Peças visuais ----------

        static Label Tag(Pilot p, VisualElement parent)
        {
            var l = Text(p.tag, "ptag", parent);
            var c = p.Color;
            l.style.backgroundColor = c;
            l.style.color = Ink(c);
            l.tooltip = p.name;
            return l;
        }

        static Color Ink(Color c)
        {
            float L(float v) => v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
            return 0.2126f * L(c.r) + 0.7152f * L(c.g) + 0.0722f * L(c.b) > 0.3f ? new Color32(20, 20, 20, 255) : Color.white;
        }

        static void Dot(Pilot p, VisualElement parent) => El("pdot", parent).style.backgroundColor = p.Color;
        static void AiBadge(Pilot p, VisualElement parent) { if (p.ai) Text($"IA {p.skill}", "aib", parent).tooltip = $"Piloto IA, qualidade {p.skill}"; }

        // Célula com sigla, nome e selo de IA.
        static VisualElement PilotCell(Pilot p, VisualElement row)
        {
            var c = El("st-cell st-cell--name", row);
            Tag(p, c);
            Text(p.name, "st-name", c);
            AiBadge(p, c);
            return c;
        }

        static VisualElement TableRow(VisualElement box, bool head = false, string extra = null) => El("st-row" + (head ? " st-head" : "") + (extra != null ? " " + extra : ""), box);
        static Label Cell(VisualElement row, string text, string cls = "") => Text(text, "st-cell " + cls, row);

        // ---------- Painel ----------

        void DrawChampSide()
        {
            if (cSetup == null) return;
            var C = store.Active;
            cSetup.style.display = C == null ? DisplayStyle.Flex : DisplayStyle.None;
            cRun.style.display = C != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (C != null) DrawRun(C); else DrawSetup();
        }

        void DrawSetup()
        {
            var D = store.draft;
            var ps = DraftPilots();
            if (!IsTyping) cName.SetValueWithoutNotify(D.name);
            cPilotCount.text = $"{ps.Count} de {Champ.MaxPilots}" + (D.faixas >= 4 && ps.Count > 0 && ps.Count < D.faixas ? $" · {D.faixas - ps.Count} {(D.faixas - ps.Count == 1 ? "trilho vazio" : "trilhos vazios")} por bateria" : "");
            cKind2.EnableInClassList("btn--on", D.faixas < 4);
            cKind4.EnableInClassList("btn--on", D.faixas == 4);
            cKind6.EnableInClassList("btn--on", D.faixas == 6);
            cKind8.EnableInClassList("btn--on", D.faixas == 8);
            cByLaps.EnableInClassList("btn--on", !D.byTime);
            cByTime.EnableInClassList("btn--on", D.byTime);

            cRoster.Clear();
            if (roster.Count == 0) Text("Nenhum piloto cadastrado. Adicione abaixo.", "small", cRoster);
            foreach (var p in roster)
            {
                var pilot = p;
                int k = D.pilots.IndexOf(p.id);
                bool on = k >= 0, dup = on && ps.Exists(q => q != p && q.color == p.color);
                var row = El("prow" + (on ? " prow--on" : ""), cRoster);
                var tg = new Toggle { value = on };
                tg.AddToClassList("prow-check");
                tg.SetEnabled(on || ps.Count < Champ.MaxPilots);
                tg.RegisterValueChangedCallback(e =>
                {
                    if (e.newValue) { if (!D.pilots.Contains(pilot.id) && D.pilots.Count < Champ.MaxPilots) D.pilots.Add(pilot.id); }
                    else D.pilots.Remove(pilot.id);
                    if (editPilot == null) pColor = null;
                    SaveStore(); DrawChampSide();
                });
                row.Add(tg);
                var info = El("prow-info", row);
                Dot(p, info);
                Text(p.name, "prow-name", info);
                Tag(p, info);
                AiBadge(p, info);
                if (on) Text($"{k + 1}º", "small", info);
                if (dup) Text("cor repetida", "small bad", info);
                var acts = El("prow-acts", row);
                Btn("Editar", () =>
                {
                    editPilot = pilot.id; pColor = pilot.color; tagTouched = true;
                    pName.SetValueWithoutNotify(pilot.name); pTag.SetValueWithoutNotify(pilot.tag);
                    SetAiForm(pilot.ai, pilot.skill);
                    DrawChampSide();
                }, "btn", acts);
                string key = "piloto:" + p.id;
                var del = Btn("Apagar", null, "btn btn--danger", acts);
                del.clicked += () =>
                {
                    if (!panel.ConfirmTwice(key)) { del.text = "Certeza?"; return; }
                    roster.Remove(pilot); ChampFiles.SavePilots(roster);
                    D.pilots.Remove(pilot.id); SaveStore();
                    if (editPilot == pilot.id) ResetPilotForm();
                    DrawChampSide();
                };
            }

            // Formulário: cores (as usadas pelos marcados ficam apagadas).
            pColor ??= FreeColor();
            var used = new HashSet<string>();
            foreach (var p in ps) if (p.id != editPilot) used.Add(p.color);
            cColors.Clear();
            for (int i = 0; i < Champ.Colors.Length; i++)
            {
                string hex = Champ.Colors[i];
                var sw = Btn("", () => { pColor = hex; DrawChampSide(); }, "swatch", cColors);
                ColorUtility.TryParseHtmlString(hex, out var col);
                sw.style.backgroundColor = col;
                sw.tooltip = Champ.ColorNames[i] + (used.Contains(hex) ? " (já usada)" : "");
                sw.EnableInClassList("swatch--on", hex == pColor);
                sw.EnableInClassList("swatch--used", used.Contains(hex));
            }
            cFormTitle.text = editPilot != null ? "EDITAR PILOTO" : "NOVO PILOTO";
            pSave.text = editPilot != null ? "Salvar piloto" : "Adicionar piloto";
            pCancel.style.display = editPilot != null ? DisplayStyle.Flex : DisplayStyle.None;

            // Etapas
            cStageCount.text = D.stages.Count > 0 ? $"{D.stages.Count} {(D.stages.Count == 1 ? "etapa" : "etapas")}" : "";
            cStages.Clear();
            if (D.stages.Count == 0) Text("Nenhuma etapa ainda.", "small", cStages);
            for (int i = 0; i < D.stages.Count; i++)
            {
                int idx = i;
                var s = D.stages[i];
                var item = El("lib-item", cStages);
                item.Add(new TrackThumb(s.data));
                var texts = El("lib-texts", item);
                Text($"{i + 1}. {s.name}", "lib-name", texts);
                Text(TrackLine(s.data), "small", texts);
                var ctl = El("stage-ctl", item);
                var step = El("stepper", ctl);
                if (D.byTime)
                {
                    // Duração da bateria, de 30 em 30 s (1 a 10 min).
                    Btn("−", () => { s.secs = Mathf.Max(60, s.secs - 30); SaveStore(); DrawChampSide(); }, "btn step-btn", step);
                    Text(Dur(s.secs), "step-val", step);
                    Btn("+", () => { s.secs = Mathf.Min(600, s.secs + 30); SaveStore(); DrawChampSide(); }, "btn step-btn", step);
                }
                else
                {
                    Btn("−", () => { s.laps = Mathf.Max(1, s.laps - 1); SaveStore(); DrawChampSide(); }, "btn step-btn", step);
                    Text($"{s.laps} {(s.laps == 1 ? "volta" : "voltas")}", "step-val", step);
                    Btn("+", () => { s.laps = Mathf.Min(30, s.laps + 1); SaveStore(); DrawChampSide(); }, "btn step-btn", step);
                }
                var mv = El("row", ctl);
                Btn("▲", () => { (D.stages[idx - 1], D.stages[idx]) = (D.stages[idx], D.stages[idx - 1]); SaveStore(); RefreshChamp(); }, "btn step-btn", mv).SetEnabled(i > 0);
                Btn("▼", () => { (D.stages[idx + 1], D.stages[idx]) = (D.stages[idx], D.stages[idx + 1]); SaveStore(); RefreshChamp(); }, "btn step-btn", mv).SetEnabled(i < D.stages.Count - 1);
                Btn("✕", () => { D.stages.RemoveAt(idx); SaveStore(); RefreshChamp(); }, "btn step-btn", mv).tooltip = "Tirar etapa";
            }

            // Pistas disponíveis (as mesmas da aba Corrida): qualquer pista fechada, adaptada ao tamanho do campeonato.
            // O recorde mostrado é o da pista nesse tamanho.
            cPick.Clear();
            int hidden = 0;
            foreach (var o in Options())
            {
                if (!o.closed) { hidden++; continue; }
                var opt = o;
                var item = new Button(() => AddStage(opt));
                item.AddToClassList("lib-item"); item.AddToClassList("pick");
                item.SetEnabled(o.closed);
                item.Add(new TrackThumb(o.save));
                var texts = El("lib-texts", item); texts.pickingMode = PickingMode.Ignore;
                Text(o.name, "lib-name", texts).pickingMode = PickingMode.Ignore;
                var rec = FindRecords(Champ.TrackHash(Adapted(o.save, D.faixas)));
                string recTxt = rec != null && rec.lap.Has ? $" · recorde {Fmt(rec.lap.t)} {rec.lap.pilot.tag}" : "";
                Text((o.sub != null ? o.sub + " · " : "") + TrackLine(o.save) + recTxt, "small", texts).pickingMode = PickingMode.Ignore;
                Text("+", "pick-plus", item).pickingMode = PickingMode.Ignore;
                cPick.Add(item);
            }
            if (hidden > 0) Text(hidden == 1 ? "1 pista aberta não aparece: feche o circuito no Construtor." : $"{hidden} pistas abertas não aparecem: feche os circuitos no Construtor.", "small", cPick);

            cRules.text = $"• Usa os ajustes da aba Corrida: velocidade <b>{Num(set.vmax)} cél/s</b>, até 100% em <b>{Num(set.ramp, 2)} s</b>, aderência <b>{Mathf.RoundToInt(set.grip)}</b>, colisão <b>{(set.collide ? "ligada" : "desligada")}</b>, turbo <b>{(set.turbo ? "ligado" : "desligado")}</b>. Ficam fixos até o fim.\n" +
                          (D.byTime
                            ? $"• <b>Por tempo</b>: cada etapa tem {(ps.Count > D.faixas ? $"<b>{Champ.HeatsPerStage(ps.Count, D.faixas, true)} grupos</b>" : "<b>uma bateria</b>")} de até {D.faixas} carros. A bateria se divide em <b>{D.faixas} manches</b>, uma em cada trilho. Vale a <b>distância</b> (voltas e fração).\n"
                            : $"• Cada etapa: {(ps.Count >= 2 ? $"<b>{Champ.HeatsPerStage(ps.Count, D.faixas, false)} baterias</b>" : "uma bateria por piloto")} de até <b>{D.faixas} carros</b>, e cada piloto corre uma vez em cada trilho. Vale o <b>tempo somado</b> de todas.\n" +
                              $"• Quem não completar as voltas tem <b>{Champ.Cutoff:0} s</b> depois da bandeirada do primeiro; fica com as voltas que fez.\n") +
                          $"• Pontos: <b>{string.Join(" · ", Champ.Points)}</b>, mais 1 para a melhor volta da etapa.";
            string prob = ChampProblem();
            cPlan.text = prob != "" ? $"<color=#F07A3A>{prob}</color>"
                : $"<b>{ps.Count} pilotos · {D.stages.Count} {(D.stages.Count == 1 ? "etapa" : "etapas")} · {Champ.HeatsPerStage(ps.Count, D.faixas, D.byTime) * D.stages.Count} baterias de {D.faixas}</b> · cerca de {Champ.EstimateMin(D.stages, ps.Count, set.vmax, D.faixas, D.byTime)} min";
            cStart.SetEnabled(prob == "");

            // Histórico
            cHistBlock.style.display = store.history.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            cHist.Clear();
            foreach (var h in store.history)
            {
                var entry = h;
                var w = h.table.Count > 0 ? h.table[0].pilot : null;
                var fold = new Foldout { text = $"{h.name}  ·  {(w != null ? w.name : "—")}  ·  {DateTimeOffset.FromUnixTimeMilliseconds(h.finished).LocalDateTime:dd/MM/yyyy}", value = false };
                fold.AddToClassList("hist");
                var t = El("stand", fold);
                var hr = TableRow(t, true); Cell(hr, "#", "st-pos"); Cell(hr, "Piloto", "st-cell--name"); Cell(hr, "Pts"); Cell(hr, "Vit.");
                for (int i = 0; i < h.table.Count; i++)
                {
                    var r = h.table[i];
                    var row = TableRow(t);
                    Cell(row, (i + 1).ToString(), "st-pos"); PilotCell(r.pilot, row); Cell(row, r.pts.ToString(), "st-pts"); Cell(row, r.wins.ToString());
                }
                Text(string.Join(" · ", h.stages), "small", fold);
                string key = "hist:" + h.id;
                var del = Btn("Apagar do histórico", null, "btn btn--danger", fold);
                del.style.marginTop = 6;
                del.clicked += () =>
                {
                    if (!panel.ConfirmTwice(key)) { del.text = "Certeza?"; return; }
                    store.history.Remove(entry); SaveStore(); DrawChampSide();
                };
                cHist.Add(fold);
            }
        }

        void AddStage(Option o)
        {
            if (!o.closed) return;
            var data = Adapted(o.save, store.draft.faixas);       // a etapa corre no tamanho do campeonato
            data.nome = o.key == "builder" ? (string.IsNullOrEmpty(o.save.nome) ? "Pista do construtor" : o.save.nome) : o.name;
            store.draft.stages.Add(new Stage { name = data.nome, laps = set.laps, secs = Mathf.Clamp(set.secs, 60, 600), data = data, hash = Champ.TrackHash(data) });
            SaveStore();
            RefreshChamp();
        }

        bool HeatNow(out Championship C, out int idx, out Heat heat, out Stage stage, out Pilot[] pilots)
        {
            C = ActiveChamp; idx = C != null ? Champ.CurrentHeat(C) : -1;
            heat = null; stage = null; pilots = null;
            if (idx < 0) return false;
            heat = C.heats[idx]; stage = C.stages[heat.s];
            pilots = new Pilot[heat.p.Length];
            for (int k = 0; k < pilots.Length; k++) pilots[k] = heat.p[k] >= 0 ? C.pilots[heat.p[k]] : null;     // null: trilho vazio
            return true;
        }

        // Dentro ou fora: o trilho mais curto é o de dentro, o mais longo o de fora. Iguais (8 simétrico, cruzamentos
        // ímpares): não diz. Na pista de 4, os do meio não dizem nada.
        string LaneSide(int k)
        {
            var L = builder.Info.laneLengths;
            if (k >= L.Length) return "";
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (float v in L) { lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); }
            if (hi - lo < 0.01f) return "";
            return Mathf.Abs(L[k] - lo) < 0.01f ? "dentro" : Mathf.Abs(L[k] - hi) < 0.01f ? "fora" : "";
        }

        void DrawRun(Championship C)
        {
            bool live = HeatNow(out _, out int idx, out var heat, out var stage, out var pl);
            int S = C.stages.Count, H = C.heats.Count;
            cTitle.text = C.name.ToUpperInvariant();
            cProgress.text = live ? $"Etapa {heat.s + 1} de {S} · bateria {idx + 1} de {H}" : $"Encerrado · {S} {(S == 1 ? "etapa" : "etapas")} · {H} baterias";

            cHeat.Clear();
            if (live)
            {
                var card = El("heat", cHeat);
                Text($"{stage.name.ToUpperInvariant()} · {StageGoal(C, stage).ToUpperInvariant()}", "heat-stage", card);
                var vs = El("vs", card);
                vs.EnableInClassList("vs--4", pl.Length == 3 || pl.Length == 4);
                vs.EnableInClassList("vs--6", pl.Length > 4);
                vs.EnableInClassList("vs--8", pl.Length > 6);
                for (int k = 0; k < pl.Length; k++)
                {
                    if (k > 0 && pl.Length == 2) Text("×", "vs-x", vs);
                    var p = pl[k];
                    if (p == null) { var empty = El("hp hp--empty", vs); Text(LaneNames[k], "small", empty); Text("—", "hp-name", empty); Text("trilho vazio", "small", empty); continue; }
                    var hp = El("hp", vs);
                    hp.style.borderLeftColor = p.Color;
                    string side = LaneSide(k);
                    Text($"{LaneNames[k]}{(side != "" ? " · " + side : "")}", "small", hp);
                    Text(p.name.ToUpperInvariant(), "hp-name", hp);
                    // As teclas seguem a ordem dos humanos (KeySet): o 1º humano usa as teclas do jogador 1, o 2º as do 2, e assim por diante.
                    int ks = 0; for (int i = 0; i < k; i++) if (pl[i] != null && !pl[i].ai) ks++;
                    Text(OnlineRun ? OnlineWho(p) : p.ai ? $"IA · qualidade {p.skill}" : ks >= Controles.Jogadores ? "só com controle" : $"acelera {KeyText(GasKeyAt(ks))} · recoloca {KeyText(ReslotKeyAt(ks))}", "small", hp);
                }
            }
            else DrawPodium(C);

            cActs.Clear();
            var ph = sim.phase;
            if (OnlineRun) DrawOnlineActs(live, idx, stage);
            else
            {
                if (!live) Btn("Arquivar e montar outro", ChampArchive, "btn btn--primary", cActs);
                else if (ph == RaceSim.Phase.Idle) Btn("Largar (Enter)", ChampStart, "btn btn--primary", cActs).SetEnabled(sim.closed);
                else if (ph == RaceSim.Phase.Done) { Btn("Salvar e seguir (Enter)", ChampSave, "btn btn--primary", cActs); Btn("Correr de novo", ChampRestart, "btn", cActs); }
                else Btn("Recomeçar bateria", ChampRestart, "btn", cActs);
                cHint.text = !live ? "Arquivar guarda a tabela final no histórico. Os recordes ficam salvos nas pistas."
                    : ph == RaceSim.Phase.Idle ? "Confira quem está em cada trilho e passe o controle. Depois, Largar e esperar as luzes."
                    : ph == RaceSim.Phase.Done ? "Salve para o resultado contar na tabela. Se algo deu errado, corra de novo: esta bateria é descartada."
                    : C.byTime ? $"Bateria de {Dur(stage.secs)} em {C.Faixas} manches: no fim de cada uma, todos trocam de trilho. Vence quem andar mais."
                    : $"Vence a bateria quem completar {stage.laps} {(stage.laps == 1 ? "volta" : "voltas")} primeiro. Quem não terminar tem {Champ.Cutoff:0} s depois da bandeirada.";
            }
            cLive.style.display = live ? DisplayStyle.Flex : DisplayStyle.None;
            if (live) DrawChampScore();

            // Etapa: a da última bateria salva (o resultado fica à vista até a próxima), ou a atual.
            int lastSaved = C.heats.FindLastIndex(x => x.Saved);
            int sIdx = lastSaved >= 0 ? C.heats[lastSaved].s : live ? heat.s : 0;
            var rows = Champ.StageResults(C, sIdx, out bool complete, out int saved, out int total);
            cStageBlock.style.display = saved > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            cStageTitle.text = $"ETAPA {sIdx + 1} · {C.stages[sIdx].name.ToUpperInvariant()}{(complete ? "" : $" ({saved} de {total} baterias)")}";
            cStageTable.Clear();
            var sh = TableRow(cStageTable, true);
            Cell(sh, "#", "st-pos"); Cell(sh, "Piloto", "st-cell--name"); if (!complete && !C.byTime) Cell(sh, "Bat."); Cell(sh, C.byTime ? "Distância" : "Voltas"); if (!C.byTime) Cell(sh, "Tempo", "st-time"); Cell(sh, "Melhor", "st-time"); Cell(sh, "Pts");
            for (int pos = 0; pos < rows.Count; pos++)
            {
                var r = rows[pos];
                var row = TableRow(cStageTable);
                Cell(row, (pos + 1).ToString(), "st-pos"); PilotCell(C.pilots[r.i], row);
                if (!complete && !C.byTime) Cell(row, $"{r.runs}/{C.Faixas}");
                if (C.byTime) Cell(row, Num(r.dist, 2));
                else { Cell(row, r.laps.ToString()); Cell(row, Fmt(r.time), "st-time"); }
                Cell(row, (r.fast ? "★ " : "") + Fmt(r.best >= 0f ? r.best : (float?)null), "st-time" + (r.fast ? " st-star" : ""));
                Cell(row, r.pts >= 0 ? r.pts.ToString() : "—", "st-pts");
            }

            // Tabela geral, com a setinha de quem subiu ou caiu na última etapa completa.
            int done = Champ.StagesDone(C);
            var prev = new Dictionary<int, int>();
            if (done >= 2) { var pt = Champ.Standings(C, done - 1); for (int i = 0; i < pt.Count; i++) prev[pt[i].i] = i; }
            var st = Champ.Standings(C);
            cTable.Clear();
            var th = TableRow(cTable, true);
            Cell(th, "#", "st-pos"); Cell(th, "Piloto", "st-cell--name"); Cell(th, "Pts"); Cell(th, "Vit."); Cell(th, "MV").tooltip = "Melhores voltas";
            for (int pos = 0; pos < st.Count; pos++)
            {
                var r = st[pos];
                var row = TableRow(cTable, false, pos == 0 && r.pts > 0 ? "st-lead" : null);
                Cell(row, (pos + 1).ToString(), "st-pos");
                var name = PilotCell(C.pilots[r.i], row);
                if (prev.TryGetValue(r.i, out int was) && was != pos) Text(was > pos ? $"▲{was - pos}" : $"▼{pos - was}", was > pos ? "st-up" : "st-down", name);
                Cell(row, r.pts.ToString(), "st-pts"); Cell(row, r.wins.ToString()); Cell(row, r.fast.ToString());
            }
            cPts.text = $"Pontos por etapa: {string.Join(" · ", System.Linq.Enumerable.Take(Champ.Points, Mathf.Clamp(C.pilots.Count, 2, Champ.Points.Length)))}, mais 1 para a melhor volta. Desempate: vitórias, pódios, tempo total.";

            // Recordes da pista em uso (os deste computador; no online cada um grava os seus, seção 39).
            {
            var cur = C.stages[live ? heat.s : sIdx];
            var rec = FindRecords(cur.hash);
            bool odd = Champ.CrossCount(cur.data) % 2 == 1;
            cRecs.Clear();
            Text(cur.name, "hint", cRecs);
            void Line(string label, Rec r)
            {
                var row = TableRow(cRecs);
                Cell(row, label, "st-label");
                var who = El("st-cell st-cell--name", row);
                if (r != null && r.Has) { Tag(r.pilot, who); Text(r.pilot.name, "st-name", who); if (r.online) Text(" online", "small", who); }
                else Text("ninguém ainda", "small", who);
                Cell(row, r != null && r.Has ? Fmt(r.t) : "—", "st-time");
            }
            Line("Volta", rec?.lap);
            if (!odd && !C.byTime) for (int k = 0; k < C.Faixas; k++) Line(LaneNames[k], rec?.Lane(k));
            if (!C.byTime) Line($"{cur.laps} voltas", rec?.Race(cur.laps));
            if (rec != null)
            {
                var bests = new List<(Pilot p, float t)>();
                foreach (var p in C.pilots) { var key = BestKey(p, OnlineRun); var b = key != null ? rec.Best(key, false) : null; if (b != null && b.lap >= 0f) bests.Add((p, b.lap)); }
                if (bests.Count > 0)
                {
                    bests.Sort((a, b) => a.t.CompareTo(b.t));
                    Text("MELHOR VOLTA DE CADA UM", "h3", cRecs);
                    foreach (var (p, t) in bests) { var row = TableRow(cRecs); PilotCell(p, row); Cell(row, Fmt(t), "st-time"); }
                }
            }

            }

            cStageList.Clear();
            for (int i = 0; i < S; i++)
            {
                var s = C.stages[i];
                var r = Champ.StageResults(C, i, out bool comp, out int sv, out int tot);
                bool isCur = live && heat.s == i;
                var item = El("lib-item" + (isCur ? " lib-item--on" : ""), cStageList);
                item.Add(new TrackThumb(s.data));
                var texts = El("lib-texts", item);
                Text($"{i + 1}. {s.name}", "lib-name", texts);
                var sub = El("row", texts);
                Text($"{StageGoal(C, s)} · ", "small", sub);
                if (comp && r.Count > 0) { Text("✓ ", "small ok", sub); Tag(C.pilots[r[0].i], sub); Text(" venceu", "small", sub); }
                else Text(sv > 0 ? $"{sv} de {tot} baterias" : isCur ? "próxima" : $"{tot} baterias", "small", sub);
            }
            cQuit.style.display = live && !OnlineRun ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // "5 voltas" ou "2 min" (campeonato por tempo).
        static string StageGoal(Championship C, Stage s) => C != null && C.byTime ? Dur(s.secs) : $"{s.laps} {(s.laps == 1 ? "volta" : "voltas")}";

        void DrawPodium(Championship C)
        {
            var T = Champ.Standings(C);
            var pod = El("podium", cHeat);
            foreach (int i in new[] { 1, 0, 2 })
            {
                if (i >= T.Count) continue;
                var p = C.pilots[T[i].i];
                var box = El("pod pod--" + (i + 1), pod);
                box.style.borderTopColor = p.Color;
                Text($"{i + 1}º", "pod-place", box);
                Text(p.name, "pod-name", box);
                Text($"{T[i].pts} pts", "small", box);
            }
            // Prêmios: mão mais rápida, rei do muro, para-choque, recordes batidos.
            Pilot fastP = null; float fastT = float.MaxValue; int fastS = 0, recCount = 0;
            var offs = new Dictionary<Pilot, int>(); var hits = new Dictionary<Pilot, int>();
            foreach (var h in C.heats)
                for (int k = 0; k < h.p.Length && h.Saved; k++)
                {
                    if (h.p[k] < 0) continue;
                    var r = h.res[k]; var p = C.pilots[h.p[k]];
                    if (r.best >= 0f && r.best < fastT) { fastT = r.best; fastP = p; fastS = h.s; }
                    offs[p] = (offs.TryGetValue(p, out int o) ? o : 0) + r.offs;
                    hits[p] = (hits.TryGetValue(p, out int x) ? x : 0) + r.hits;
                    if (r.rec || r.raceRec) recCount++;
                }
            void Award(string label, Pilot p, string text)
            {
                var row = El("award", cHeat);
                Text(label, "award-label", row);
                if (p != null) Tag(p, row);
                Text(text, "body", row);
            }
            Pilot Most(Dictionary<Pilot, int> d, out int n) { Pilot best = null; n = 0; foreach (var kv in d) if (kv.Value > n) { n = kv.Value; best = kv.Key; } return best; }
            if (fastP != null) Award("Mão mais rápida", fastP, $" {Fmt(fastT)} em {C.stages[fastS].name}");
            var wall = Most(offs, out int nOffs); if (wall != null) Award("Rei do muro", wall, $" {nOffs} {(nOffs == 1 ? "saída" : "saídas")} de pista");
            var bump = Most(hits, out int nHits); if (bump != null) Award("Para-choque", bump, $" {nHits} {(nHits == 1 ? "toque" : "toques")}");
            Award("Recordes batidos", null, recCount.ToString());
        }

        void DrawChampScore()
        {
            cScore.Clear();
            if (!HeatNow(out _, out _, out _, out _, out var pl)) return;
            var head = El("score-row score-head", cScore);
            foreach (var h in new[] { "Piloto", sim.ByTime ? "Distância" : "Voltas", "Última", "Melhor", "Saídas", "Toques" }) Text(h, h == "Piloto" ? "score-cell score-cell--name" : "score-cell", head);
            for (int k = 0; k < pl.Length && k < sim.N; k++)
            {
                if (pl[k] == null) continue;
                var c = sim.cars[k];
                var r = El("score-row" + (sim.winner == k ? " score-win" : ""), cScore);
                var name = Text($"{pl[k].tag}{(sim.winner == k ? " · 1º" : "")}", "score-cell score-cell--name", r);
                name.style.color = pl[k].Color;
                name.style.unityFontStyleAndWeight = FontStyle.Bold;
                foreach (var v in new[] { LapsCell(k), Fmt(c.lastLap), Fmt(c.best), c.offs.ToString(), c.hits.ToString() }) Text(v, "score-cell", r);
            }
        }

        // ---------- Corrida do campeonato ----------

        void UseChampTrack()
        {
            TrackSave d = null;
            if (HeatNow(out var C, out _, out _, out var stage, out var pl))
            {
                d = stage.data;
                sim.set = new RaceSettings { laps = stage.laps, vmax = C.rules.vmax, ramp = C.rules.ramp, grip = C.rules.grip, collide = C.rules.collide, turbo = C.rules.turbo, byTime = C.byTime, secs = stage.secs };
                sim.cutoff = C.byTime ? (float?)null : Champ.Cutoff;
                for (int k = 0; k < RaceSim.MaxCars; k++) sim.setup[k] = k < pl.Length && pl[k] != null ? pl[k].carro.Clamped : default;   // seção 48
                for (int k = 0; k < RaceSim.MaxCars; k++)
                {
                    sim.present[k] = k >= pl.Length || pl[k] != null;
                    sim.aiSkill[k] = k < pl.Length && pl[k] != null && pl[k].ai ? pl[k].skill : 0;
                }
            }
            else
            {
                sim.set = set; sim.cutoff = null; System.Array.Clear(sim.aiSkill, 0, sim.aiSkill.Length); System.Array.Clear(sim.setup, 0, sim.setup.Length);
                for (int k = 0; k < RaceSim.MaxCars; k++) sim.present[k] = true;
                var list = C != null ? C.stages : store.draft.stages;
                d = list.Count > 0 ? list[list.Count - 1].data : builderStash;     // prévia: a última etapa incluída
            }
            if (d != null)
            {
                string before = builder.Info != null ? Champ.TrackHash(builder.ToSave()) : "";
                builder.ShowTrack(TrackSave.FromJson(d.ToJson(false)));
                if (Champ.TrackHash(d) != before) panel.FrameTrack();
            }
            DrawPadHeads();
        }

        void RefreshChamp()
        {
            UseChampTrack();
            sim.Reset();
            scoreDirty = champDirty = true;
            DrawChampSide();
        }

        void ChampStart() { if (HeatNow(out _, out _, out _, out _, out _) && sim.phase == RaceSim.Phase.Idle && !CarPickOpen(out _, out _)) StartRace(); }
        void ChampRestart() { sim.Reset(); scoreDirty = champDirty = true; }
        void ChampEnter() { if (CarPickOpen(out var pc, out int ps)) CarPickAll(pc, ps); else if (sim.phase == RaceSim.Phase.Idle) ChampStart(); else if (sim.phase == RaceSim.Phase.Done) ChampSave(); }

        void ChampBegin()
        {
            if (ChampProblem() != "") return;
            var D = store.draft;
            var ps = DraftPilots();
            var C = new Championship
            {
                id = Champ.NewId("c"), name = string.IsNullOrWhiteSpace(D.name) ? $"Campeonato {store.history.Count + 1}" : D.name.Trim(), created = Champ.Now,
                rules = new ChampRules { vmax = set.vmax, ramp = set.ramp, grip = set.grip, collide = set.collide, turbo = set.turbo },
                faixas = D.faixas, byTime = D.byTime,
                heats = Champ.MakeHeats(ps.Count, D.stages.Count, D.faixas, D.byTime, UnityEngine.Random.Range(1, int.MaxValue)),
            };
            foreach (var p in ps) C.pilots.Add(p.Copy());
            foreach (var s in D.stages) C.stages.Add(new Stage { name = s.name, laps = s.laps, secs = s.secs, hash = s.hash, data = TrackSave.FromJson(s.data.ToJson(false)) });
            store.active = C; store.hasActive = true;
            SaveStore();
            RefreshChamp();
        }

        TrackRecords FindRecords(string hash, bool create = false, string name = null)
        {
            var r = recs.tracks.Find(x => x.hash == hash);
            if (r == null && create) { r = new TrackRecords { hash = hash, name = name }; recs.tracks.Add(r); }
            return r;
        }

        // Volta completa: avisa na hora se é recorde da pista ou o melhor pessoal (vale de verdade ao salvar a bateria).
        void OnLapDone(int k, float lap)
        {
            if (!panel.ChampMode) return;
            LapFlash(k, lap, sim.cars[k].best ?? float.PositiveInfinity, sim.HumanBest());
        }

        // carBest e humanBest: a melhor volta do carrinho e a de gente na bateria, antes desta volta.
        void LapFlash(int k, float lap, float carBest, float humanBest)
        {
            if (!HeatNow(out _, out _, out _, out var stage, out var pl)) return;
            var R = FindRecords(stage.hash);
            float track = Mathf.Min(R != null && R.lap.Has ? R.lap.t : float.PositiveInfinity, humanBest);
            if (k >= pl.Length || pl[k] == null) return;
            string key = BestKey(pl[k], OnlineRun);
            var b = key != null ? R?.Best(key, false) : null;
            float own = Mathf.Min(b != null && b.lap >= 0f ? b.lap : float.PositiveInfinity, carBest);
            var pad = pads[k];
            if (!pl[k].ai && lap < track) { pad.flash = $"Recorde da pista! {Fmt(lap)}"; pad.flashRec = true; pad.flashT = 2.5f; }
            else if (!float.IsPositiveInfinity(own) && lap < own) { pad.flash = $"Melhor volta pessoal! {Fmt(lap)}"; pad.flashRec = false; pad.flashT = 2.5f; }
        }

        // Salva o resultado da bateria, atualiza os recordes e prepara a próxima.
        void ChampSave()
        {
            if (sim.phase != RaceSim.Phase.Done || !HeatNow(out var C, out int idx, out var heat, out _, out var pl)) return;
            var resList = SimResults(C, pl);
            HeatRecords(C, idx, resList, false);
            heat.res = resList;
            if (Champ.CurrentHeat(C) < 0) C.finished = Champ.Now;
            SaveStore();
            RefreshChamp();
        }

        // O resultado da bateria a partir do RaceSim (no online, o do visitante é o espelho do host: serve para mostrar).
        HeatResult[] SimResults(Championship C, Pilot[] pl)
        {
            var res = new HeatResult[pl.Length];
            for (int k = 0; k < pl.Length; k++)
            {
                var c = sim.cars[k];
                res[k] = pl[k] == null ? new HeatResult { dnf = true }      // trilho vazio
                    : new HeatResult { laps = c.laps, time = c.finish ?? sim.t, dnf = !c.finish.HasValue && !C.byTime, best = c.best ?? -1f, fin = c.finish ?? -1f, offs = c.offs, hits = c.hits, dist = Mathf.Max(0f, sim.Dist(k)) };
            }
            return res;
        }

        // No fim da bateria, embaixo do resultado: a tabela geral como fica com esta bateria (ainda não salva) e a posição
        // de cada um na etapa. Os pontos da etapa só entram quando ela termina (Champ.Standings).
        void DrawChampStandings(VisualElement into)
        {
            if (!HeatNow(out var C, out _, out var heat, out var stage, out var pl)) return;
            var keep = heat.res;
            heat.res = SimResults(C, pl);
            var st = Champ.Standings(C);
            var sr = Champ.StageResults(C, heat.s, out bool complete, out int saved, out int total);
            heat.res = keep;
            var stagePos = new Dictionary<int, int>();
            for (int i = 0; i < sr.Count; i++) stagePos[sr[i].i] = i;
            Text(complete ? $"TABELA GERAL · etapa {stage.name} encerrada" : $"TABELA GERAL · etapa {stage.name}: {saved} de {total} baterias", "res-head", into);
            for (int pos = 0; pos < st.Count; pos++)
            {
                var r = st[pos]; var p = C.pilots[r.i];
                var row = El("res-row res-row--small" + (pos == 0 && r.pts > 0 ? " res-row--win" : ""), into);
                Text($"{pos + 1}º", "res-pos", row);
                El("hud-dot", row).style.backgroundColor = p.Color;
                Text(p.name, "res-name", row);
                Text($"{r.pts} pts", "res-cell", row);
                string etapa = "";
                if (stagePos.TryGetValue(r.i, out int sp))
                {
                    var s = sr[sp];
                    etapa = $"etapa {sp + 1}º · " + (C.byTime ? $"{Num(s.dist, 2)} v" : $"{s.laps} v {Fmt(s.time)}");
                    if (complete && s.pts >= 0) etapa += $" · +{s.pts}";
                }
                Text(etapa, "res-cell res-cell--dim", row);
            }
        }

        // A chave do melhor pessoal: o id do piloto no offline; no online, o id fixo do jogador (sem ele, não guarda).
        static string BestKey(Pilot p, bool online) => !online ? p.id : string.IsNullOrEmpty(p.uid) ? null : "u:" + p.uid;

        // Grava os recordes de uma bateria a partir do resultado dela (best e fin de cada trilho) e marca rec, raceRec e pb.
        // O offline, o host e cada visitante do online fazem a mesma conta, cada um no seu recordes.json (seção 39).
        void HeatRecords(Championship C, int idx, HeatResult[] res, bool online)
        {
            var heat = C.heats[idx]; var stage = C.stages[heat.s];
            var pl = new Pilot[heat.p.Length];
            for (int k = 0; k < pl.Length; k++) pl[k] = heat.p[k] >= 0 && heat.p[k] < C.pilots.Count ? C.pilots[heat.p[k]] : null;
            var R = FindRecords(stage.hash, true, stage.name);
            R.name = stage.name;
            bool odd = Champ.CrossCount(stage.data) % 2 == 1;
            int L = stage.laps;
            long now = Champ.Now;
            string host = online ? OnlineHostName() : null;
            float prevLap = R.lap.Has ? R.lap.t : float.PositiveInfinity;
            var prevRaceRec = R.Race(L);
            float prevRace = prevRaceRec != null && prevRaceRec.Has ? prevRaceRec.t : float.PositiveInfinity;
            // Recordes da pista são de gente: piloto IA só guarda o melhor pessoal dele.
            float heatBest = float.PositiveInfinity, heatFin = float.PositiveInfinity;
            for (int k = 0; k < pl.Length && k < res.Length; k++)
            {
                if (pl[k] == null || pl[k].ai || res[k] == null) continue;
                if (res[k].best >= 0f) heatBest = Mathf.Min(heatBest, res[k].best);
                if (res[k].fin >= 0f) heatFin = Mathf.Min(heatFin, res[k].fin);
            }
            var news = new List<string>();
            for (int k = 0; k < pl.Length && k < res.Length; k++)
            {
                var p = pl[k]; var r = res[k];
                if (p == null || r == null) continue;
                r.rec = r.raceRec = r.pb = false;                 // o online recebe as marcas do host: aqui valem as nossas
                Rec Make(float t) => new Rec { t = t, pilot = p.Copy(), date = now, champ = C.name, lane = k, online = online, host = host };
                string key = BestKey(p, online);
                var pb = key != null ? R.Best(key, true) : null;
                if (r.best >= 0f)
                {
                    float best = r.best;
                    if (!p.ai)
                    {
                        if (best == heatBest && best < prevLap) { R.lap = Make(best); r.rec = true; news.Add($"recorde de volta de {p.name} ({Fmt(best)})"); }
                        // Com cruzamentos em número ímpar o carrinho passa pelos dois trilhos: recorde por trilho não faz sentido.
                        // Por tempo o carrinho passa por todos os trilhos: também não há recorde por trilho.
                        if (!odd && !C.byTime && (!R.Lane(k).Has || best < R.Lane(k).t)) R.SetLane(k, Make(best));
                    }
                    if (pb != null && (pb.lap < 0f || best < pb.lap)) { r.pb = pb.lap >= 0f; pb.lap = best; }
                }
                if (r.fin >= 0f)
                {
                    float fin = r.fin;
                    if (!p.ai && fin == heatFin && fin < prevRace) { R.SetRace(L, Make(fin)); r.raceRec = true; news.Add($"recorde de {L} voltas de {p.name} ({Fmt(fin)})"); }
                    if (pb != null)
                    {
                        var mine = pb.race.Find(x => x.laps == L);
                        if (mine == null) pb.race.Add(new RaceRec { laps = L, rec = Make(fin) });
                        else if (fin < mine.rec.t) mine.rec = Make(fin);
                    }
                }
            }
            ChampFiles.SaveRecords(recs);
            if (news.Count > 0) panel.Toast("Novo " + string.Join(" e novo ", news) + ".");
        }

        void ChampArchive()
        {
            var C = store.Active;
            if (C == null || Champ.CurrentHeat(C) >= 0) return;
            var h = new HistoryEntry { id = C.id, name = C.name, finished = C.finished > 0 ? C.finished : Champ.Now };
            foreach (var s in C.stages) h.stages.Add($"{s.name} ({StageGoal(C, s)})");
            foreach (var r in Champ.Standings(C)) h.table.Add(new HistoryRow { pilot = C.pilots[r.i].Copy(), pts = r.pts, wins = r.wins, podiums = r.podiums, fast = r.fast });
            store.history.Insert(0, h);
            if (store.history.Count > 30) store.history.RemoveRange(30, store.history.Count - 30);
            store.hasActive = false; store.active = null;
            SaveStore();
            RefreshChamp();
        }

        void ChampStatus()
        {
            var C = ActiveChamp;
            if (C == null) { panel.SetStatus("Monte o campeonato: pilotos e etapas", ""); return; }
            if (!HeatNow(out _, out int idx, out var heat, out _, out _))
            {
                var w = C.pilots[Champ.Standings(C)[0].i];
                panel.SetStatus($"{C.name}: {w.name} venceu", "status--ok");
                return;
            }
            string where = $"Etapa {heat.s + 1} de {C.stages.Count} · bateria {idx + 1} de {C.heats.Count}";
            if (sim.phase == RaceSim.Phase.Switch) panel.SetStatus($"{where} · troca de trilho", "");
            else if (sim.phase == RaceSim.Phase.Running) panel.SetStatus(sim.ByTime ? $"{where} · {MancheText} · faltam {Fmt(sim.TimeLeft)}" : $"{where} · volta {Mathf.Min(MaxLaps() + 1, sim.set.laps)} de {sim.set.laps}", "");
            else if (sim.phase == RaceSim.Phase.Done) panel.SetStatus(OnlineRun && !IsHost ? $"Bateria {idx + 1} encerrada · o host salva e segue" : $"Bateria {idx + 1} encerrada · salve para seguir", "status--ok");
            else if (sim.phase == RaceSim.Phase.Countdown) panel.SetStatus("Atenção à largada", "");
            else panel.SetStatus(where, "");
        }

        bool ChampBanner(out string title, out string sub)
        {
            title = sub = "";
            var C = ActiveChamp;
            if (C == null) return false;
            if (!HeatNow(out _, out int idx, out var heat, out var stage, out var pl))
            {
                var top = Champ.Standings(C)[0];
                title = $"{C.pilots[top.i].name} vence o campeonato";
                sub = $"{top.pts} pontos · {top.wins} {(top.wins == 1 ? "vitória" : "vitórias")} · {C.name}";
                return true;
            }
            if (sim.phase == RaceSim.Phase.Done && sim.winner >= 0)
            {
                var c = sim.cars[sim.winner];
                title = $"{pl[sim.winner].name} vence a bateria";
                sub = $"{Fmt(c.finish)} · melhor volta {Fmt(c.best)}";
                // Recorde de volta na bateria: pode ter sido de quem perdeu, então diz de quem é.
                var R = FindRecords(stage.hash);
                float hb = sim.HumanBest();
                if (!float.IsPositiveInfinity(hb) && (R == null || !R.lap.Has || hb < R.lap.t))
                    for (int k = 0; k < pl.Length; k++) if (pl[k] != null && !pl[k].ai && sim.cars[k].best == hb) { sub += $" · RECORDE {pl[k].tag} {Fmt(hb)}"; break; }
                return true;
            }
            // Primeira bateria de uma etapa nova: mostra quem venceu a anterior.
            if (sim.phase == RaceSim.Phase.Idle && idx > 0 && C.heats[idx - 1].s != heat.s)
            {
                int prev = C.heats[idx - 1].s;
                var rows = Champ.StageResults(C, prev, out _, out _, out _);
                title = $"{C.pilots[rows[0].i].name} vence a etapa {C.stages[prev].name}";
                sub = $"Próxima: {stage.name} · {StageGoal(C, stage)}";
                return true;
            }
            return false;
        }

        // ---------- Controles e carrinhos com a cor do piloto ----------

        void DrawPadHeads()
        {
            Pilot[] pl = null;
            bool champ = ChampLike, live = champ && HeatNow(out _, out _, out _, out _, out pl);
            int lane = OnlineRun ? MyLane() : -1;                // online: cada um só tem o controle do próprio carrinho
            padsBox.style.display = panel.RaceMode && (!champ || live) && (!OnlineRun || lane >= 0) ? DisplayStyle.Flex : DisplayStyle.None;
            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                var p = pads[k];
                p.root.style.display = k < sim.N && sim.present[k] && (!OnlineRun || IsLocalLane(k)) && (!SplitOn || sim.aiSkill[k] == 0) ? DisplayStyle.Flex : DisplayStyle.None;
                if (k >= sim.N || !sim.present[k]) continue;
                var pilot = live && k < pl.Length ? pl[k] : null;
                bool ai = sim.aiSkill[k] > 0;
                p.name.text = pilot != null ? $"{pilot.name.ToUpperInvariant()} · {LaneNames[k].ToUpperInvariant()}"
                    : ai ? $"ROBÔ · {LaneNames[k].ToUpperInvariant()}" : $"JOGADOR {k + 1}";
                // O ajuste do carro ao lado do nome (só com até 4 painéis: nos de 6 e 8 não cabe).
                if (!sim.setup[k].Padrao && sim.N <= 4) p.name.text += $"  <size=10><color=#9AA3A8>{sim.setup[k].Resumo}</color></size>";
                p.gasText.text = ai && !OnlineRun ? $"IA PILOTANDO · {sim.aiSkill[k]}" : GasLabel(k);
                p.reslot.text = OnlineRun ? $"Recolocar  {KeyText(ReslotKeyAt(LocalKeySet(Math.Max(0, LocalOf(pilot?.id)))))}" : $"Recolocar  {KeyText(ReslotKey(k))}";
                p.gas.EnableInClassList("gas--ai", ai);
                if (pilot != null)
                {
                    var col = pilot.Color;
                    p.root.style.borderTopColor = col;
                    p.gas.style.borderTopColor = p.gas.style.borderBottomColor = p.gas.style.borderLeftColor = p.gas.style.borderRightColor = col;
                    p.fill.style.backgroundColor = new Color(col.r, col.g, col.b, 0.35f);
                    carViews[k].SetColors(col);
                }
                else
                {
                    p.root.style.borderTopColor = StyleKeyword.Null;
                    p.gas.style.borderTopColor = p.gas.style.borderBottomColor = p.gas.style.borderLeftColor = p.gas.style.borderRightColor = StyleKeyword.Null;
                    p.fill.style.backgroundColor = StyleKeyword.Null;
                    carViews[k].SetColors(LaneColors[k]);
                }
            }
        }

        // A mesma pintura de carrinho-vermelho.png (ferramentas/gen-texturas.js), na cor do piloto.
        Texture2D CarTexture(string key, Color body)
        {
            if (carTex.TryGetValue(key, out var cached) && cached) return cached;
            const int W = 320, H = 192, SS = 2;
            Color dark = new Color(body.r * 0.67f, body.g * 0.67f, body.b * 0.67f, 1f);
            Color tire = new Color32(0x14, 0x17, 0x16, 255), edge = new Color32(0xF2, 0xF2, 0xEC, 255), glass = new Color32(0x1B, 0x27, 0x30, 255), glassHi = new Color32(0x5C, 0x73, 0x83, 255);
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int j = 0; j < SS; j++)
                        for (int i = 0; i < SS; i++)
                        {
                            // Coordenadas da imagem original (640 × 384, y para baixo), centradas e em milésimos.
                            float X = (x + (i + 0.5f) / SS) * 2f, Y = (y + (j + 0.5f) / SS) * 2f;
                            var c = CarPixel((X - 320f) / 1000f, (Y - 192f) / 1000f, body, dark, tire, edge, glass, glassHi);
                            r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a;
                        }
                    px[(H - 1 - y) * W + x] = a > 0f ? new Color(r / a, g / a, b / a, a / (SS * SS)) : new Color(0, 0, 0, 0);
                }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "Carrinho " + key, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(px);
            tex.Apply(true);
            carTex[key] = tex;
            return tex;
        }

        static Color CarPixel(float px, float py, Color body, Color dark, Color tire, Color edge, Color glass, Color glassHi)
        {
            var c = new Color(0, 0, 0, 0);
            float sh = RBox(px - 0.01f, py - 0.012f, 0, 0, 0.29f, 0.15f, 0.07f);                        // sombra suave
            if (sh < 0.022f) c = Over(c, Color.black, 0.28f * Mathf.Min(1f, (0.022f - sh) / 0.022f));
            foreach (float wx in new[] { -0.19f, 0.19f })
                foreach (float wy in new[] { -0.148f, 0.148f })
                    if (RBox(px, py, wx, wy, 0.052f, 0.03f, 0.01f) < 0f) c = Over(c, tire, 1f);
            if (RBox(px, py, 0, 0, 0.285f, 0.14f, 0.07f) < 0f)
            {
                c = Over(c, body, 1f);
                if (Mathf.Abs(py) < 0.022f && px > 0.14f) c = Over(c, edge, 1f);                         // faixa no capô
                if (RBox(px, py, 0.09f, 0, 0.058f, 0.095f, 0.035f) < 0f)                                  // para-brisa
                    c = Over(c, Mathf.Abs((px - 0.1f) - (py + 0.03f) * 0.6f) < 0.009f ? glassHi : glass, 1f);
                if (new Vector2(px + 0.08f, py).magnitude < 0.058f) c = Over(c, edge, 1f);                // círculo do número
            }
            if (RBox(px, py, -0.272f, 0, 0.026f, 0.158f, 0.008f) < 0f) c = Over(c, dark, 1f);           // aerofólio
            return c;
        }

        static float RBox(float px, float py, float cx, float cy, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(px - cx) - hx + r, qy = Mathf.Abs(py - cy) - hy + r;
            return new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        static Color Over(Color dst, Color col, float alpha)
        {
            float a = alpha + dst.a * (1f - alpha);
            if (a <= 0f) return new Color(0, 0, 0, 0);
            return new Color((col.r * alpha + dst.r * dst.a * (1f - alpha)) / a, (col.g * alpha + dst.g * dst.a * (1f - alpha)) / a, (col.b * alpha + dst.b * dst.a * (1f - alpha)) / a, a);
        }
    }
}
