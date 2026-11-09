using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using System.IO;

namespace Autorama.Editor
{
    /// <summary>
    /// Copia steam_appid.txt para a pasta de build após compilar.
    /// Necessário para a Steam API inicializar corretamente.
    /// </summary>
    public class SteamBuildPostprocessor : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            string projectRoot = Path.GetDirectoryName(report.summary.outputPath);
            string sourceFile = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "steam_appid.txt");
            string targetFile = Path.Combine(projectRoot, "steam_appid.txt");

            if (File.Exists(sourceFile))
            {
                File.Copy(sourceFile, targetFile, overwrite: true);
                UnityEngine.Debug.Log($"✓ steam_appid.txt copiado para: {targetFile}");
            }
            else
            {
                UnityEngine.Debug.LogWarning($"⚠ steam_appid.txt não encontrado em: {sourceFile}");
            }
        }
    }
}
