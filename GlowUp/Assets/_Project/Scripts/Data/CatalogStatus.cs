using System;
using System.Threading.Tasks;
using GlowUp.Net;

namespace GlowUp.Data
{
    /// <summary>
    /// Answers one question: "is everything the app needs for scanning WITHOUT internet on this phone?"
    /// It counts the products, brands and ingredients saved on the phone and says when they were last checked against
    /// the database. The Settings page shows it (row "Offline data") with a "Download now" button.
    ///
    /// Before a presentation on weak wifi: open Settings while online, tap Download now, wait for "ready", then try a
    /// scan in airplane mode. Scanning, the result and Recent Scans work offline for everything the phone knows.
    /// </summary>
    public static class CatalogStatus
    {
        public struct Info
        {
            /// <summary>Products the app may use (make-up, Active) / every product entry on the phone.</summary>
            public int usableProducts, products;
            public int brands, ingredients;
            /// <summary>When the lists were last checked against the database, and last downloaded whole (this phone's clock; 0 = never).</summary>
            public long lastCheckedUnix, lastFullUnix;
            /// <summary>The phone holds products and ingredients: scanning works without internet.</summary>
            public bool ready;
        }

        /// <summary>What is on the phone right now. Nothing is downloaded.</summary>
        public static Info Now()
        {
            CatalogSync.State p = ProductRepository.SyncState, b = BrandRepository.SyncState, i = IngredientRepository.SyncState;
            var info = new Info
            {
                usableProducts = ProductRepository.Usable().Count, products = ProductRepository.All().Count,
                brands = BrandRepository.All().Count, ingredients = IngredientRepository.Count,
                lastCheckedUnix = Math.Min(p.lastCheckedUnix, Math.Min(b.lastCheckedUnix, i.lastCheckedUnix)),
                lastFullUnix = Math.Min(p.lastFullUnix, Math.Min(b.lastFullUnix, i.lastFullUnix))
            };
            info.ready = info.usableProducts > 0 && info.ingredients > 0;
            return info;
        }

        /// <summary>One line for the Settings page: "447 make-up products, 42 brands and 134 ingredients on this phone. Checked 3 minutes ago."</summary>
        public static string Describe(Info info)
        {
            if (info.products == 0 && info.ingredients == 0)
                return "Nothing is saved on this phone yet. Go online and tap Download now.";
            string what = $"{info.usableProducts} make-up products, {info.brands} brands and {info.ingredients} ingredients on this phone.";
            string when = info.lastCheckedUnix > 0 ? " Checked " + CatalogSync.Ago(info.lastCheckedUnix) + "." : " Never checked against the database.";
            return what + when + (info.ready ? " Scanning works without internet." : "");
        }

        /// <summary>
        /// Downloads the three lists whole, now (Settings > Download now). True when all three were downloaded.
        /// Offline, or when the database does not answer in time, the copies on the phone stay as they are.
        /// </summary>
        public static async Task<bool> DownloadEverything()
        {
            if (!ConnectivityMonitor.Reachable) return false;
            long before = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 2;
            await IngredientRepository.LoadAll(true);
            await ProductRepository.LoadActive(true);          // loads the brands as well
            Info after = Now();
            return after.lastFullUnix >= before;
        }
    }
}
