using System;
using System.Web.UI;
namespace LegacyShop.Web
{
    public partial class Default : Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            var count = Session["CartCount"];
            var file = Server.MapPath("~/App_Data/data.xml");
        }
    }
}
