using System;
using System.Configuration;
using System.Data.SqlClient;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Web;

namespace LegacyShop.Core
{
    // NOTE: BinaryFormatter in this comment must not be counted
    /* HttpContext.Current in a block comment
       must not be counted either */
    public class CartService
    {
        private const string Hint = "HttpContext.Current is used below"; // string must not count
        private const string Path = @"C:\temp\BinaryFormatter.txt";

        public string CurrentUser => HttpContext.Current.User.Identity.Name;

        public byte[] Serialize(object cart)
        {
            var formatter = new BinaryFormatter();
            using (var ms = new MemoryStream()) { formatter.Serialize(ms, cart); return ms.ToArray(); }
        }

        public SqlConnection Open() => new SqlConnection(ConfigurationManager.ConnectionStrings["Shop"].ConnectionString);

        public object CachedPrices() => HttpRuntime.Cache["prices"];
    }
}
