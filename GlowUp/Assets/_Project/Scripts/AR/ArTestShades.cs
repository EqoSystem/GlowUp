using GlowUp.Scan;

namespace GlowUp.AR
{
    /// <summary>
    /// A small built-in list of TEST shades for checking AR Try-On on a phone without internet or database: different
    /// undertones, depths, finishes and parts of the face. Used only by the debug overlay ("Next test shade"), never shown to
    /// users. The codes and names are made up for testing; they are not real products.
    /// </summary>
    public static class ArTestShades
    {
        public struct Shade
        {
            public string code, name, hex, finish, undertone, area;
            public float coverage;
            public Shade(string code, string name, string hex, string finish, string undertone, string area, float coverage)
            { this.code = code; this.name = name; this.hex = hex; this.finish = finish; this.undertone = undertone; this.area = area; this.coverage = coverage; }
        }

        public static readonly Shade[] All =
        {
            new Shade("T01", "Classic red",     "#B3202E", "matte",    "neutral", ShadeAdvisor.Lips,   0.85f),
            new Shade("T02", "Cool berry",      "#7E2448", "satin",    "cool",    ShadeAdvisor.Lips,   0.75f),
            new Shade("T03", "Warm nude",       "#B9786A", "glossy",   "warm",    ShadeAdvisor.Lips,   0.65f),
            new Shade("T04", "Pale pink gloss", "#F2A7B8", "glossy",   "cool",    ShadeAdvisor.Lips,   0.55f),
            new Shade("T05", "Peach blush",     "#F08F7A", "satin",    "warm",    ShadeAdvisor.Cheeks, 0.6f),
            new Shade("T06", "Bronze shimmer",  "#9C6A45", "shimmer",  "warm",    ShadeAdvisor.Eyes,   0.7f),
            new Shade("T07", "Plum metallic",   "#5E3A66", "metallic", "cool",    ShadeAdvisor.Eyes,   0.7f),
            new Shade("T08", "Soft brown brow", "#5A4434", "matte",    "neutral", ShadeAdvisor.Brows,  0.7f),
            new Shade("T09", "Deep foundation", "#6B4A3A", "matte",    "warm",    ShadeAdvisor.Face,   0.5f)
        };
    }
}
