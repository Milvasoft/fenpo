using Fenpo.Bluetooth.Pbap;

namespace Fenpo.Bluetooth.Tests.Pbap;

public sealed class VCardTests
{
    [Fact]
    public void ReadsNamesAndNumbersFromVersion3()
    {
        const string book = "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Ayşe Yılmaz\r\nN:Yılmaz;Ayşe;;;\r\nTEL;TYPE=CELL:+90 555 000 00 02\r\nitem1.TEL:0212 000 00 00\r\nEND:VCARD\r\n"
            + "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:\r\nN:Kaya;Mert;;;\r\nTEL;TYPE=CELL:05551112233\r\nEND:VCARD\r\n"
            + "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Numarasız\r\nEND:VCARD\r\n";

        var contacts = VCard.Parse(book);

        Assert.Equal(2, contacts.Count);
        Assert.Equal("Ayşe Yılmaz", contacts[0].Name);
        Assert.Equal(["+90 555 000 00 02", "0212 000 00 00"], contacts[0].Numbers);
        Assert.Equal("Mert Kaya", contacts[1].Name);
    }

    [Fact]
    public void UnfoldsAndDecodesVersion21QuotedPrintable()
    {
        const string book = "BEGIN:VCARD\r\nVERSION:2.1\r\nFN;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:Ay=C5=9Fe =\r\nY=C4=B1lmaz\r\nTEL;CELL:+905550000002\r\nEND:VCARD\r\n";

        var contact = Assert.Single(VCard.Parse(book));

        Assert.Equal("Ayşe Yılmaz", contact.Name);
    }

    [Fact]
    public void FoldedVersion3LinesJoin()
    {
        const string book = "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Çok Uzun Bir\r\n  İsim\r\nTEL:+905550000002\r\nEND:VCARD\r\n";

        Assert.Equal("Çok Uzun Bir İsim", Assert.Single(VCard.Parse(book)).Name);
    }

    [Fact]
    public void NumbersMapToNamesByNormalizedNumber()
    {
        var names = VCard.NamesByNumber([new PhoneContact("Ayşe", ["+90 555 000 00 02", "Kısa"]), new PhoneContact("Başka", ["05550000002"])]);

        Assert.Equal("Ayşe", Assert.Single(names).Value);
        Assert.Equal("5550000002", names.Keys.Single());
    }
}
