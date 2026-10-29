<%@ Page Language="C#" MasterPageFile="~/Site.Master" CodeBehind="Cart.aspx.cs" Inherits="LegacyShop.Web.Cart" %>
<asp:Content ID="Main" ContentPlaceHolderID="MainContent" runat="server">
    <asp:ScriptManager ID="Scripts" runat="server" />
    <asp:UpdatePanel ID="CartPanel" runat="server">
        <ContentTemplate>
            <asp:GridView ID="CartGrid" runat="server" AutoGenerateColumns="false" OnRowCommand="CartGrid_RowCommand">
                <Columns>
                    <asp:BoundField DataField="Name" HeaderText="Product" />
                    <asp:ButtonField CommandName="Remove" Text="Remove" />
                </Columns>
            </asp:GridView>
            <asp:Label ID="Total" runat="server" />
            <asp:Button ID="Checkout" runat="server" Text="Checkout" OnClick="Checkout_Click" />
        </ContentTemplate>
    </asp:UpdatePanel>
</asp:Content>
