using System;
using System.IO;
using UnityEngine;

namespace Autorama
{
    // O fantasma de uma pista (seção 55): a melhor volta limpa de gente na corrida livre, com o carro e a física da hora.
    [Serializable]
    public class GhostFile
    {
        public float lap = -1f;
        public long date;
        public int lane;
        public CarSetup carro;
        public float vmax, grip, ramp;
        public string tape;      // CarTape em Base64
    }

    // fantasmas/<identidade da pista>.json, ao lado de recordes.json.
    public static class GhostStore
    {
        static string Dir => Path.Combine(TrackLibrary.Root, "fantasmas");
        static string PathOf(string hash) => Path.Combine(Dir, hash + ".json");

        public static GhostFile Load(string hash)
        {
            if (string.IsNullOrEmpty(hash)) return null;
            string p = PathOf(hash);
            if (!File.Exists(p)) return null;
            try { var g = JsonUtility.FromJson<GhostFile>(File.ReadAllText(p)); return g != null && g.lap >= 0f ? g : null; }
            catch (Exception e) { Debug.LogWarning($"Fantasma ilegível ({p}): {e.Message}"); return null; }
        }

        public static void Save(string hash, GhostFile g)
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(PathOf(hash), JsonUtility.ToJson(g));
        }
    }
}
