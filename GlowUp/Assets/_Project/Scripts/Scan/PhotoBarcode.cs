using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GlowUp.Data;
using UnityEngine;

namespace GlowUp.Scan
{
    /// <summary>
    /// BARCODE STEP OF PHOTO CAPTURE (11 November 2026). A barcode names ONE product for certain, so it is tried before the text:
    ///   1. the barcode in the photo is read with the phone's own reader (Google ML Kit, no internet);
    ///   2. the product with that number is looked for in the catalogue (the website's "barcode" field of a product), then in
    ///      what this phone learned (PhotoBarcodeMemory);
    ///   3. no barcode, or no product with it: the text is read and matched as before, and when the user then picks a product the
    ///      number is remembered on this phone for it.
    /// Photo only: the live scan's own barcode code (SmartScanFlowController, ProductIndex) is not touched.
    /// </summary>
    public static class PhotoBarcode
    {
        const float ReadSeconds = 5f;

        /// <summary>The key (digits, no leading zeros) of the barcode in `picture`, or "" when none was seen or the reader is not in this build.</summary>
        public static async Task<string> Read(Texture2D picture)
        {
            if (picture == null) return "";
            if (!MlKitBarcodeReader.TryCreate(out MlKitBarcodeReader reader))
            {
                { }
                return "";
            }
            var done = new TaskCompletionSource<string>();
            try { reader.Read(picture, code => done.TrySetResult(code ?? "")); }
            catch (Exception e) { { } return ""; }
            Task first = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(ReadSeconds)));
            string raw = first == done.Task ? done.Task.Result : "";
            string key = MlKitBarcodeReader.Key(raw);
            { }
            return key;
        }

        /// <summary>The catalogue's product with this number, else the one this phone learned; null when neither knows it.</summary>
        public static ProductData Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return ProductRepository.Index.ByBarcode(key) ?? PhotoBarcodeMemory.Find(key);
        }
    }

    /// <summary>
    /// The barcode numbers THIS PHONE learned: when the user picked a product after a photo that showed a barcode, the number is kept
    /// for that product, so the next photo of it is found at once. Only on this phone (the app may not write products); the website's
    /// "barcode" field of a product stays the shared, checked place for a number.
    /// </summary>
    public static class PhotoBarcodeMemory
    {
        const string CacheKey = "photo_barcodes";
        const int Limit = 300;

        [Serializable] class Entry { public string key = "", productId = ""; public long learnedAtUnix; }
        [Serializable] class Holder { public List<Entry> items = new List<Entry>(); }

        static Holder holder;
        static Holder H() { if (holder == null && !OfflineCache.TryLoad(CacheKey, out holder)) holder = new Holder(); return holder ?? (holder = new Holder()); }

        public static int Count => H().items.Count;

        /// <summary>The learned product with this barcode key, or null (also when that product left the catalogue).</summary>
        public static ProductData Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (Entry e in H().items)
                if (e != null && e.key == key) return ProductRepository.Index.ById(e.productId);
            return null;
        }

        /// <summary>Keeps `key` for `product` (a barcode already known to the catalogue is not kept: the catalogue wins). Returns false when nothing was kept.</summary>
        public static bool Learn(string key, ProductData product)
        {
            if (string.IsNullOrEmpty(key) || product == null || string.IsNullOrEmpty(product.id)) return false;
            if (ProductRepository.Index.ByBarcode(key) != null) return false;
            List<Entry> items = H().items;
            items.RemoveAll(old => old == null || old.key == key);
            items.Add(new Entry { key = key, productId = product.id, learnedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            while (items.Count > Limit) items.RemoveAt(0);
            OfflineCache.Save(CacheKey, H());
            return true;
        }
    }
}
