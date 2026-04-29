using System.Xml;

namespace LooseNotes.Web.Services;

public interface IXmlIngestService
{
    XmlDocument LoadHardenedDocument(Stream xml);
}

// PRD §22 stipulates XML processing with default parser settings (DTD allowed,
// external entity resolution active). FIASSE rejects this — XXE is one of the
// most common platform-level vulnerabilities. We expose a single hardened
// loader and use it for every XML ingestion path.
//
// FIASSE: Modifiability (centralized parser policy), Integrity (boundary).
public sealed class XmlIngestService : IXmlIngestService
{
    public XmlDocument LoadHardenedDocument(Stream xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = 1_000_000,
            CloseInput = false
        };
        using var reader = XmlReader.Create(xml, settings);
        var doc = new XmlDocument { XmlResolver = null };
        doc.Load(reader);
        return doc;
    }
}
