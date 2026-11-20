using UnityEngine;
using UnityEngine.InputSystem;

namespace Autorama
{
    // Teclado e mouse do construtor (os mesmos atalhos do HTML).
    public class BuilderInput : MonoBehaviour
    {
        public TrackBuilder builder;
        public TopDownCamera cam;

        int pressedIndex = -1, pressedEnf = -1;
        bool dragging;
        Vector3 grabWorld;
        Vector2 grabOrigin, pressScreen, grabEnf;

        void Update()
        {
            var kb = Keyboard.current;
            var m = Mouse.current;
            if (kb == null || m == null || builder.Segments.Count == 0 || BuilderPanel.EditLocked) return;   // fora do construtor, só os controles da aba em uso

            // Com o campo de nome em foco, as teclas são do texto.
            if (!BuilderPanel.IsTyping) HandleKeys(kb);
            HandleMouse(m);
        }

        void HandleKeys(Keyboard kb)
        {
            bool ctrl = kb.ctrlKey.isPressed;
            if (ctrl)
            {
                if (kb.zKey.wasPressedThisFrame) { if (kb.shiftKey.isPressed) builder.Redo(); else builder.Undo(); }
                if (kb.yKey.wasPressedThisFrame) builder.Redo();
                return;
            }
            foreach (var e in TrackBuilder.Palette)
                if (kb[e.key].wasPressedThisFrame) builder.Add(e);

            // Enfeite armado ou selecionado (seção 97): R gira, Del tira, Esc solta.
            if (builder.Armed != null || builder.EnfSel >= 0)
            {
                if (kb.rKey.wasPressedThisFrame) builder.RotateEnfeite();
                if (kb.deleteKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) builder.RemoveEnfeite();
                if (kb.escapeKey.wasPressedThisFrame) { if (builder.Armed != null) builder.Disarm(); else builder.SelectEnfeite(-1); }
                return;
            }

            if (kb.fKey.wasPressedThisFrame) builder.FlipSelected();
            if (kb.eKey.wasPressedThisFrame) builder.SetBack(!builder.Back);
            if (kb.rKey.wasPressedThisFrame) builder.RotateStart();
            if (kb.deleteKey.wasPressedThisFrame) builder.RemoveSelected();
            if (kb.backspaceKey.wasPressedThisFrame) { if (builder.Selected > 0) builder.RemoveSelected(); else builder.RemoveLast(); }
            if (kb.escapeKey.wasPressedThisFrame && builder.Selected >= 0) builder.Select(-1);
        }

        // Clique seleciona a peça (as próximas encaixam depois dela). Arrastar a largada move a pista inteira.
        void HandleMouse(Mouse m)
        {
            Vector2 screen = m.position.ReadValue();
            if (builder.Armed != null)
            {
                bool ui = BuilderPanel.PointerOverUI(screen);
                Vector3 w = cam.MouseOnTable();
                Vector2 cell = new Vector2(w.x - builder.transform.position.x, w.z - builder.transform.position.z) / builder.Scale;
                builder.ShowArmed(ui ? (Vector2?)null : cell);
                if (m.leftButton.wasPressedThisFrame && !ui) builder.PlaceArmed(cell);
                return;
            }
            if (m.leftButton.wasPressedThisFrame)
            {
                if (BuilderPanel.PointerOverUI(screen)) { pressedIndex = -2; return; }
                pressedIndex = -1;
                Ray ray = Camera.main.ScreenPointToRay(screen);
                pressedEnf = -1;
                if (Physics.Raycast(ray, out var hit, 500f)) { pressedIndex = builder.IndexOf(hit.collider); pressedEnf = builder.IndexOfEnfeite(hit.collider); }
                if (pressedEnf >= 0) pressedIndex = -3;
                grabEnf = pressedEnf >= 0 ? builder.enfeites[pressedEnf].pos : default;
                dragging = false;
                pressScreen = screen;
                grabWorld = cam.MouseOnTable();
                grabOrigin = builder.Origin.pos;
            }
            else if (m.leftButton.isPressed && pressedEnf >= 0)
            {
                if (!dragging && (screen - pressScreen).sqrMagnitude > 16f) { dragging = true; builder.BeginEdit(); }
                if (dragging)
                {
                    Vector3 d = (cam.MouseOnTable() - grabWorld) / builder.Scale;
                    builder.MoveEnfeite(pressedEnf, grabEnf + new Vector2(d.x, d.z));
                }
            }
            else if (m.leftButton.isPressed && pressedIndex == 0)
            {
                if (!dragging && (screen - pressScreen).sqrMagnitude > 16f) { dragging = true; builder.BeginEdit(); }
                if (dragging)
                {
                    Vector3 d = (cam.MouseOnTable() - grabWorld) / builder.Scale;     // em células
                    builder.MoveStart(grabOrigin + new Vector2(d.x, d.z));
                }
            }
            else if (m.leftButton.wasReleasedThisFrame)
            {
                if (pressedEnf >= 0 && !dragging) builder.SelectEnfeite(pressedEnf);
                else if (pressedIndex >= -1 && !dragging) builder.Select(pressedIndex);
                pressedEnf = -1;
                if (dragging) builder.EndEdit();
                pressedIndex = -2;
                dragging = false;
            }
        }
    }
}
