extern alias inspector;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using inspector::PcInspector.Collectors;

namespace JvtechTests;

/// <summary>Nom et e-mail de l'utilisateur du PC : tri des valeurs lues (données simulées).</summary>
[TestClass]
public class UserIdentityTests
{
    [TestMethod]
    public void NomsEtAdresses_SansDoublonNiMelange()
    {
        var r = new UserIdentity.Result();
        r.AddName("Jean Dupont", "Office / Outlook");
        r.AddName("jean dupont", "compte Windows");
        r.AddName("jean.dupont@exemple.fr", "Office / Outlook");
        r.AddName("  ", "compte Windows");
        r.AddEmail("Jean.Dupont@Exemple.fr", "Office / Outlook");
        r.AddEmail("jean.dupont@exemple.fr", "compte Microsoft");
        r.AddEmail("pas une adresse", "compte Microsoft");

        Assert.AreEqual(1, r.Names.Count);
        Assert.AreEqual("Jean Dupont", r.Names[0].Value);
        Assert.AreEqual("Office / Outlook", r.Names[0].Source);
        Assert.AreEqual(1, r.Emails.Count);
        Assert.AreEqual("jean.dupont@exemple.fr", r.Emails[0].Value);
    }
}
