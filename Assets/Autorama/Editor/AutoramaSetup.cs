using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Autorama.EditorTools
{
    // Autorama > Montar cena do construtor: configura as texturas, cria os materiais e o tema,
    // e monta a cena com mesa, pista, câmera de cima, luz com sombra e o painel.
    public static class AutoramaSetup
    {
        const string Root = "Assets/Autorama";
        const string Tex = Root + "/Texturas";
        const string Mat = Root + "/Materiais";
        const string ScenePath = Root + "/Cenas/Construtor.unity";

        [MenuItem("Autorama/Montar cena do construtor")]
        public static void BuildAll()
        {
            ConfigureTextures();
            var theme = CreateTheme(out var boardMat);
            CreateScene(theme, boardMat);
            Debug.Log("Autorama: cena do construtor pronta em " + ScenePath + ". Aperte Play.");
        }

        // Só texturas, materiais e ícones (peças novas): não mexe na cena.
        [MenuItem("Autorama/Atualizar materiais e ícones")]
        public static void RefreshMaterials()
        {
            ConfigureTextures();
            CreateTheme(out _);
            Debug.Log("Autorama: materiais e ícones atualizados.");
        }

        // ---------- Texturas ----------

        static void ConfigureTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Tex }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                string name = Path.GetFileNameWithoutExtension(path);
                bool repeat = name.StartsWith("pista-") || name.StartsWith("pista4-") || name.StartsWith("grade-mesa") || name == "mureta4"
                              || name == "asfalto" || name == "zebra" || name == "sobreposicao";
                imp.textureType = TextureImporterType.Default;
                imp.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                imp.alphaIsTransparency = !repeat;
                imp.mipmapEnabled = true;
                imp.filterMode = FilterMode.Trilinear;
                imp.anisoLevel = 8;
                imp.maxTextureSize = name.StartsWith("pista4-") ? 2048 : 1024;     // a pista de 4 tem o dobro de detalhe por célula
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.SaveAndReimport();
            }
        }

        static Texture2D T(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Tex}/{name}.png");

        // ---------- Materiais ----------

        static Material GetOrCreate(string name, string shader)
        {
            Directory.CreateDirectory(Mat);
            string path = $"{Mat}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
            else m.shader = Shader.Find(shader);
            return m;
        }

        // URP/Lit fosco: recebe e projeta sombra (é a sombra que mostra a altura da ponte vista de cima).
        static Material Lit(string name, Texture2D tex, Color color)
        {
            var m = GetOrCreate(name, "Universal Render Pipeline/Lit");
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", 0.08f);
            m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Unlit(string name, Texture2D tex, Color color, bool transparent)
        {
            var m = GetOrCreate(name, "Universal Render Pipeline/Unlit");
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", color);
            if (transparent) MakeTransparent(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        // Carrinho: URP/Lit com recorte pela transparência da imagem; a sombra sai com o formato do carrinho.
        static Material CarMaterial(string name, Texture2D tex)
        {
            var m = Lit(name, tex, Color.white);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", 0f);                                  // dos dois lados
            m.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material SparkMaterial()
        {
            var m = GetOrCreate("Faisca", "Universal Render Pipeline/Particles/Unlit");
            m.SetColor("_BaseColor", new Color(1f, 0.85f, 0.25f));
            EditorUtility.SetDirty(m);
            return m;
        }

        static void MakeTransparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        static TrackTheme CreateTheme(out Material boardMat)
        {
            string path = Root + "/TemaPista.asset";
            var theme = AssetDatabase.LoadAssetAtPath<TrackTheme>(path);
            if (theme == null) { theme = ScriptableObject.CreateInstance<TrackTheme>(); AssetDatabase.CreateAsset(theme, path); }

            theme.straight   = Lit("Pista Reta", T("pista-reta"), Color.white);
            theme.curveRight = Lit("Pista Curva Direita", T("pista-curva-direita"), Color.white);
            theme.curveLeft  = Lit("Pista Curva Esquerda", T("pista-curva-esquerda"), Color.white);
            theme.bankRight  = Lit("Pista Inclinada Direita", T("pista-curva-inclinada-direita"), Color.white);
            theme.bankLeft   = Lit("Pista Inclinada Esquerda", T("pista-curva-inclinada-esquerda"), Color.white);
            theme.cross      = Lit("Pista Cruzamento", T("pista-cruzamento"), Color.white);
            theme.crossShort = Lit("Pista Cruzamento Curto", T("pista-cruzamento-curto"), Color.white);
            theme.checker    = Lit("Bandeirada", T("bandeirada"), Color.white);
            theme.guard      = Lit("Mureta", null, new Color32(0xD9, 0xDC, 0xD6, 0xFF));
            theme.support    = Lit("Pilar", null, new Color32(0x8A, 0x91, 0x96, 0xFF));
            theme.arrow      = Unlit("Seta", T("seta-insercao"), Color.white, true);
            theme.selected   = Unlit("Selecao", null, new Color(0.96f, 0.76f, 0.1f, 0.35f), true);
            theme.overlap    = Unlit("Sobreposicao", T("sobreposicao"), Color.white, true);
            boardMat         = Lit("Mesa", T("grade-mesa"), Color.white);

            // Pista de 4 carros: arte própria (seção 25 do guia).
            theme.straight4    = Lit("Pista4 Reta", T("pista4-reta"), Color.white);
            theme.curveRight4  = Lit("Pista4 Curva Direita", T("pista4-curva-direita"), Color.white);
            theme.curveLeft4   = Lit("Pista4 Curva Esquerda", T("pista4-curva-esquerda"), Color.white);
            theme.bankRight4   = Lit("Pista4 Inclinada Direita", T("pista4-curva-inclinada-direita"), Color.white);
            theme.bankLeft4    = Lit("Pista4 Inclinada Esquerda", T("pista4-curva-inclinada-esquerda"), Color.white);
            theme.cross4       = Lit("Pista4 Cruzamento", T("pista4-cruzamento"), Color.white);
            theme.crossShort4  = Lit("Pista4 Cruzamento Curto", T("pista4-cruzamento-curto"), Color.white);
            theme.start4       = Lit("Pista4 Largada", T("pista4-largada"), Color.white);
            theme.guard4       = Lit("Mureta4", T("mureta4"), Color.white);
            theme.support4     = Lit("Pilar4", null, new Color32(0x3A, 0x44, 0x4E, 0xFF));
            theme.board4       = Lit("Mesa4", T("grade-mesa4"), Color.white);
            theme.cars34       = new[] { CarMaterial("Carrinho Verde", T("carrinho-verde")), CarMaterial("Carrinho Amarelo", T("carrinho-amarelo")) };

            theme.icons.Clear();
            foreach (var prefix in new[] { "icone-", "icone4-" })
                foreach (var guid in AssetDatabase.FindAssets(prefix + " t:Texture2D", new[] { Tex }))
                {
                    var t = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
                    if (t != null && t.name.StartsWith(prefix) && !theme.icons.Contains(t)) theme.icons.Add(t);
                }
            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();
            return theme;
        }

        // ---------- Painel (UI Toolkit) ----------

        // PanelSettings com o tema padrão de runtime; escala pela altura da tela (referência 1600×900).
        static PanelSettings CreatePanelSettings()
        {
            Directory.CreateDirectory(Root + "/UI");
            string tssPath = Root + "/UI/TemaRuntime.tss";
            if (!File.Exists(tssPath))
            {
                File.WriteAllText(tssPath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(tssPath);
            }
            string path = Root + "/UI/PainelConstrutor.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (ps == null) { ps = ScriptableObject.CreateInstance<PanelSettings>(); AssetDatabase.CreateAsset(ps, path); }
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(tssPath);
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1600, 900);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 1f;
            EditorUtility.SetDirty(ps);
            AssetDatabase.SaveAssets();
            return ps;
        }

        // ---------- Cena ----------

        static void CreateScene(TrackTheme theme, Material boardMat)
        {
            var active = SceneManager.GetActiveScene();
            if (active.isDirty && !string.IsNullOrEmpty(active.path)) EditorSceneManager.SaveScene(active);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Ambiente + sol somam ~1 na mesa clara: a sombra (só ambiente) fica visível, e é ela que mostra a altura da ponte.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.46f, 0.47f, 0.46f);

            var camGo = new GameObject("Câmera de cima", typeof(Camera), typeof(TopDownCamera));
            camGo.tag = "MainCamera";
            camGo.transform.SetPositionAndRotation(new Vector3(16f, 30f, 10f), Quaternion.Euler(90f, 0f, 0f));
            var cam = camGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 11f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(0xE8, 0xEC, 0xE6, 0xFF);

            // Sol baixo e vindo de cima da tela: a sombra cai para baixo (e um pouco para a direita).
            // Assim ela aparece de lado tanto nas diagonais quanto nas retas horizontais.
            var lightGo = new GameObject("Sol", typeof(Light));
            lightGo.transform.rotation = Quaternion.Euler(40f, 168f, 0f);
            var light = lightGo.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.85f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.8f;

            var boardGo = new GameObject("Mesa", typeof(GridBoard));
            var board = boardGo.GetComponent<GridBoard>();
            board.material = boardMat;

            var trackGo = new GameObject("Pista", typeof(TrackBuilder));
            var builder = trackGo.GetComponent<TrackBuilder>();
            builder.theme = theme;
            builder.board = board;

            var inputGo = new GameObject("Controles", typeof(BuilderInput));
            var input = inputGo.GetComponent<BuilderInput>();
            input.builder = builder;
            input.cam = camGo.GetComponent<TopDownCamera>();

            var panelGo = new GameObject("Painel", typeof(UIDocument), typeof(BuilderPanel));
            panelGo.GetComponent<UIDocument>().panelSettings = CreatePanelSettings();
            var panel = panelGo.GetComponent<BuilderPanel>();
            panel.builder = builder;
            panel.cam = camGo.GetComponent<TopDownCamera>();
            panel.styles = AssetDatabase.LoadAssetAtPath<StyleSheet>(Root + "/UI/Construtor.uss");

            var raceGo = new GameObject("Corrida", typeof(RaceController));
            var race = raceGo.GetComponent<RaceController>();
            race.builder = builder;
            race.panel = panel;
            race.cam = camGo.GetComponent<TopDownCamera>();
            race.carMaterials = new[] { CarMaterial("Carrinho Vermelho", T("carrinho-vermelho")), CarMaterial("Carrinho Azul", T("carrinho-azul")) };
            race.sparkMaterial = SparkMaterial();

            // A UI Toolkit recebe mouse e teclado do Input System por este EventSystem.
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            // Construtor e mesa rodam também no Editor: religar faz eles desenharem já com os materiais.
            board.enabled = false; board.enabled = true;
            builder.enabled = false; builder.enabled = true;

            EditorSceneManager.SaveScene(scene, ScenePath);

            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            list.RemoveAll(s => s.path == ScenePath);
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
