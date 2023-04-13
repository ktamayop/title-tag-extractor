using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace TitleTagExtractor.Extensions
{
    public static class XDocumentExtensions
    {
        public static IEnumerable<XElement> Flatten(this XElement element)
        {
            // first return ourselves
            yield return new XElement(
                element.Name,

                // Output our text if we have no elements
                !element.HasElements ? element.Value : null,

                // Or the flattened sequence of our children if they exist
                element.Elements().SelectMany(el => el.Flatten()));

            // Then return our own attributes (that aren't xmlns related)
            foreach (var attribute in element.Attributes().Where(aa => !aa.IsNamespaceDeclaration))
            {
                // check if the attribute has a namespace,// if not we "borrow" our element's
                var isNone = attribute.Name.Namespace == XNamespace.None;
                yield return new XElement(!isNone ? attribute.Name : element.Name.Namespace + attribute.Name.LocalName, attribute.Value);
            }
        }

        public static XElement Flatten(this XDocument document)
        {
            // used to fix the naming of the namespaces
            var ns = document.Root.Attributes()
                .Where(aa => aa.IsNamespaceDeclaration
                             && aa.Name.LocalName != "xmlns")
                .Select(aa => new { aa.Name.LocalName, aa.Value });
            return new XElement(
                document.Root.Name,

                // preserve "specific" xml namespaces
                ns.Select(n => new XAttribute(XNamespace.Xmlns + n.LocalName, n.Value)),

                // place root attributes right after the root element
                document.Root.Attributes()
                    .Where(aa => !aa.IsNamespaceDeclaration)
                    .Select(aa => new XAttribute(aa.Name, aa.Value)),

                // then flatten our children
                document.Root.Elements().SelectMany(el => el.Flatten()));
        }
    }
}
