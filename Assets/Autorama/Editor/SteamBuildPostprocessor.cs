using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Autorama.Editor
{
    /// <summary>
    /// Copia steam_appid.txt para a pasta de build após compilar.
    /// Necessário para a Steam API inicializar corretamente.
    /// A Steam procura o arquivo ao lado do executável. No Windows e no Linux é a pasta da build; no macOS o executável
    /// fica dentro do pacote (Autorama.app/Contents/MacOS), então o arquivo vai para lá (e também ao lado do .app,
    /// que é onde a Steam o encontra quando ela mesma abre o jogo). Seção 24 do guia.
    /// </summary>
    public class SteamBuildPostprocessor : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            string sourceFile = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "steam_appid.txt");
            if (!File.Exists(sourceFile))
            {
                UnityEngine.Debug.LogWarning($"⚠ steam_appid.txt não encontrado em: {sourceFile}");
                return;
            }

            string output = report.summary.outputPath;
            var targets = new List<string>();
            if (report.summary.platform == BuildTarget.StandaloneOSX)
            {
                // outputPath é o .app. Ao lado do executável (dentro do pacote) e ao lado do pacote.
                string macOS = Path.Combine(output, "Contents", "MacOS");
                if (Directory.Exists(macOS)) targets.Add(Path.Combine(macOS, "steam_appid.txt"));
                targets.Add(Path.Combine(Path.GetDirectoryName(output.TrimEnd('/', '\\')), "steam_appid.txt"));
            }
            else targets.Add(Path.Combine(Path.GetDirectoryName(output), "steam_appid.txt"));

            foreach (var target in targets)
            {
                File.Copy(sourceFile, target, overwrite: true);
                UnityEngine.Debug.Log($"✓ steam_appid.txt copiado para: {target}");
            }
        }
    }
}
