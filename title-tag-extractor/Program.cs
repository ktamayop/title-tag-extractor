using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;
using TitleTagExtractor.Extensions;

namespace TitleTagExtractor
{
    public class Program
    {
        private static void Main(string[] args)
        {
            //NOTE: To debug
#if DEBUG
            args = new[]{
                //"-h",
                "-s", //show file names
                //"-f", //flatten results,
                "-t", //truncate long items
                @"src:C:\dev\working\transformer\samples",
                "xpaths:(//Synopsis/TypeCode[text()=\"LOGLN\" and ../LanguageCode=\"EN\"])/../*"
                //"xpaths:(//Synopsis/TypeCode[text()=\"LOGLN\" and ../LanguageCode=\"EN\" and ../SourceId=\"60013830\"])/../*"
                //"xpaths://Title/TypeCode|//Title/LevelCode"
            };
#endif

            new Program()
                .Run(args);

            Console.WriteLine("\n");

            if (Debugger.IsAttached)
                Console.ReadLine();
        }

        private void Run(IEnumerable<string> args)
        {
            MainAsync(args.ToList()).GetAwaiter().GetResult();
        }

        private async Task MainAsync(List<string> argsList)
        {
            Debugger.Break();

            var (showFileName, displayEmptyRows, flattenResults, truncateLongItems, xPaths, src) = ParseFlags(argsList);
            if (src == null)
                return;

            var dir = new DirectoryInfo(src);
            if (!dir.Exists)
            {
                Console.WriteLine("The source directory was not found.");
                return;
            }

            var sampleFiles = dir.GetFiles("*.xml", SearchOption.TopDirectoryOnly);
            if (!sampleFiles.Any())
            {
                Console.WriteLine("The source directory contains no xml files.");
                return;
            }

            foreach (var xPath in xPaths)
            {
                var queryResults = new QueryResults
                {
                    Xpath = xPath,
                    ShowFileNames = showFileName,
                    DisplayEmptyRows = displayEmptyRows,
                    FlattenResults = flattenResults,
                    TruncateLongItems = truncateLongItems
                };

                foreach (var file in sampleFiles)
                {
                    try
                    {
                        var doc = await GetXDocument(file);

                        var elementGroups = doc.XPathSelectElements(xPath)
                            .GroupBy(x => x.Name)
                            .ToList();

                        //add the headers only once. They're all the same for each xpath.
                        if (queryResults.Headers == null && elementGroups.Any())
                            queryResults.Headers = elementGroups
                                .Select(e => e.Key.LocalName.ToString())
                                .ToArray();

                        //add the file name
                        queryResults.FileNames.Add(file.Name);

                        var elements = elementGroups
                            .Select(g => g.ToList())
                            .ToList();

                        if (!elementGroups.Any())
                        {
                            //add an empty matrix. This file has no matches.
                            queryResults.Data.Add(new string[0, 0]);
                            continue;
                        }

                        //create the matrix for this file
                        var totalRows = elements[0].Count;
                        var totalColumns = elements.Count;
                        var matrix = new string[totalRows, totalColumns];

                        for (var i = 0; i < totalRows; i++)
                            for (var j = 0; j < totalColumns; j++)
                                matrix[i, j] = elements[j][i].Value;

                        //add this matrix (file results) to the results (xpath query)
                        queryResults.Data.Add(matrix);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"{file.Name} -> Failed!. Error: {e.Message}");
                    }
                }

                AssembleResults(queryResults, showFileName);
                Console.WriteLine();
            }
        }

        private static void AssembleResults(QueryResults queryResults, bool showFileName)
        {
            //merge all matrices into one
            var mergedMatrix = new string[queryResults.TotalRows + 1, queryResults.TotalCols];

            //add the headers to the first row
            var firstColumnHeader = queryResults.ShowFileNames ? new[] { "File Name" } : new string[0];
            var headers = firstColumnHeader
                .Concat(queryResults.Headers)
                .ToList();

            int[] mergingCol = { 0 };
            headers.ForEach(x => mergedMatrix[0, mergingCol[0]++] = x);
            mergingCol[0] = showFileName ? 1 : 0; //reset the merging col
            var mergingRow = 1;
            for (var i = 0; i < queryResults.Data.Count; i++)
            {
                var matrix = queryResults.Data[i];

                var totalCols = matrix.GetLength(1);
                var totalRows = matrix.GetLength(0);
                var flattenedMatrix = new string[1, totalCols];
                if (queryResults.FlattenResults && totalRows > 1)
                {
                    //flatten this matrix before print it.
                    for (var col = 0; col < totalCols; col++)
                    {
                        var colValues = new List<string>();
                        for (var row = 0; row < totalRows; row++)
                        {
                            var value = matrix[row, col];
                            if (!string.IsNullOrEmpty(value))
                                colValues.Add((value ?? "").Replace("\n", "").Replace("\r", ""));
                        }

                        flattenedMatrix[0, col] = string.Join(", ", colValues.Distinct());
                    }

                    matrix = flattenedMatrix;
                    totalRows = flattenedMatrix.GetLength(0);
                }

                if (showFileName)
                    mergedMatrix[mergingRow, 0] = queryResults.FileNames[i];

                Merge(matrix, mergedMatrix, mergingRow, mergingCol[0]);
                mergingRow += totalRows;
                if (totalRows == 0)
                    mergingRow++;
            }

            CalculateColumnPaddings(mergedMatrix, queryResults);
            PrintMatrix(mergedMatrix, queryResults);
        }

        private static void CalculateColumnPaddings(string[,] matrix, QueryResults queryResults)
        {
            //calculate the length of the largest item in each column
            var totalRows = matrix.GetLength(0);
            var totalCols = matrix.GetLength(1);
            var paddingData = new(int col, int length, int padding)[totalCols];
            var startRow = queryResults.TruncateLongItems ? 1 : 0;

            for (var col = 0; col < totalCols; col++)
            {
                var max = int.MinValue;
                //ignore the headers for the calculation.
                for (var row = startRow; row < totalRows; row++)
                {
                    var itemLength = (matrix[row, col] ?? "").Length;
                    if (itemLength > max)
                    {
                        max = itemLength;
                    }
                }

                //max is the length of the largest element in the column
                paddingData[col] = (col, max, 0);
            }

            //sort the columns ascending so we resolve padding to accomodate 
            //the shortest column first and leave the remainding space to the largest columns.
            paddingData = paddingData.OrderBy(x => x.length).ToArray();

            //start resolving the padding for the columns
            var availableSpace = Console.BufferWidth - totalCols + 1;
            var maxColumnPadding = availableSpace / totalCols;
            var columnsToDistribute = totalCols;

            for (var i = 0; i < paddingData.Length; i++)
            {
                var padding = Math.Min(maxColumnPadding, paddingData[i].length);
                paddingData[i].padding += padding;

                //substract the space needed for this column of the available space and recalculate the max column padding with one column less.
                availableSpace -= padding;
                columnsToDistribute--;

                if (columnsToDistribute > 0)
                    maxColumnPadding = (int)Math.Round((double)availableSpace / columnsToDistribute);
            }

            var columnPaddings = paddingData.OrderBy(x => x.col).Select(x => x.padding).ToArray();
            var totalSpace = columnPaddings.Sum();
            availableSpace = Console.BufferWidth - totalCols + 1;
            while (totalSpace > availableSpace)
            {
                Debugger.Break();
                for (var i = 0; i < columnPaddings.Length; i++)
                {
                    if (columnPaddings[i] > 2)
                        columnPaddings[i]--; //decrease the padding for each element 

                    totalSpace = columnPaddings.Sum();
                    if (totalSpace < availableSpace)
                        break;
                }
            }

            queryResults.ColumnPaddings.AddRange(columnPaddings);
        }

        private static void Merge(string[,] sourceMatrix, string[,] targetMatrix, int rowOffset, int colOffset)
        {
            for (var i = 0; i < sourceMatrix.GetLength(0); i++)
                for (var j = 0; j < sourceMatrix.GetLength(1); j++)
                    targetMatrix[i + rowOffset, j + colOffset] = (sourceMatrix[i, j] ?? "").Replace("\n", "").Replace("\r", "");
        }

        private static void PrintMatrix(string[,] matrix, QueryResults queryResults)
        {
            //only set cursor position if we display all values as they are. If -t is set we don't allow redirection through | tee log.txt for instance.
            var cursorTopOffset = queryResults.TruncateLongItems ? Console.CursorTop : -1;
            PrintLine(ref cursorTopOffset, "XPATH", queryResults.Xpath, LineSeparatorMode.AfterContent);

            var count = 0;
            for (var i = 0; i < matrix.GetLength(0); i++)
            {
                //print all row items.
                var rowItems = new string[matrix.GetLength(1)];
                for (var j = 0; j < matrix.GetLength(1); j++)
                {
                    //fill the file name if the first column is empty.
                    if (queryResults.ShowFileNames && string.IsNullOrEmpty(matrix[i, 0]))
                        matrix[i, 0] = matrix[i - 1, 0];

                    var item = matrix[i, j] ?? "";
                    if (queryResults.TruncateLongItems && item.Length > queryResults.ColumnPaddings[j])
                        item = item.Substring(0, queryResults.ColumnPaddings[j]);

                    rowItems[j] = item.PadRight(queryResults.ColumnPaddings[j]);
                }

                var emptyRow = rowItems
                    .Skip(queryResults.ShowFileNames ? 1 : 0)
                    .Select(x => x.Trim())
                    .All(string.IsNullOrEmpty);

                if (!queryResults.DisplayEmptyRows && emptyRow)
                    continue;

                var row = string.Join("|", rowItems);
                PrintLine(ref cursorTopOffset, content: $"{row}");
                count++;

                //print a line after the header row
                if (i == 0)
                    PrintLine(ref cursorTopOffset, lineSeparatorMode: LineSeparatorMode.BeforeContent);
            }

            //subtract 1 to not count header row as an item.
            PrintLine(ref cursorTopOffset, content: $"Displaying {count - 1} out of {matrix.GetLength(0) - 1} total items.", lineSeparatorMode: LineSeparatorMode.BeforeContent);
        }

        private (bool showFileName, bool displayEmptyRows, bool flattenResults, bool truncateLongItems, string[] xPaths, string src) ParseFlags(List<string> argsList)
        {
            var help = argsList.Contains("-h") || argsList.Contains("--help");
            if (help)
            {
                ShowUsage();
                return (false, false, false, false, null, null);
            }

            Console.WriteLine("\n" +
                              $"Title Tag Extractor v.{this.GetApplicationVersion()}" +
                              "\n-------------------------------" +
                              "\n");

            // Parse all the flags.
            var showFileName = ProcessFlag(argsList, "-s", false);
            if (!showFileName)
                showFileName = ProcessFlag(argsList, "--show-filename", false);

            var displayEmptyRows = ProcessFlag(argsList, "-d", false);
            if (!displayEmptyRows)
                displayEmptyRows = ProcessFlag(argsList, "--display-empty-rows", false);

            var flattenResults = ProcessFlag(argsList, "--flatten-results", false);
            if (!flattenResults)
                flattenResults = ProcessFlag(argsList, "-f", false);

            var truncateLongItems = ProcessFlag(argsList, "--truncate-long-items", false);
            if (!truncateLongItems)
                truncateLongItems = ProcessFlag(argsList, "-t", false);

            var src = ProcessFlag(argsList, "src:", ".");

            var xPaths = ProcessArrayFlag<string>(argsList, "xpaths:");

            //return
            return (showFileName, displayEmptyRows, flattenResults, truncateLongItems, xPaths, src);
        }

        private static void ShowUsage()
        {
            Console.WriteLine(
                "Usage: title-tag-extractor [options]" +
                "\n" +
                "\nOptions:" +
                "\n-h, --help                               Show help information." +
                "\n-s, --show-filename                      Set this flag to display the file name of the processed file at the first column of the results table." +
                "\n-d, --display-empty-rows                 Set this flag to display the rows even when no results are found." +
                "\n-f, --flatten-results                    Set this flag to display the results flattened for each file. If a query has several results, they'll be displayed on a single line, separated by commas." +
                "\n-t, --truncate-long-items                Set this flag to fit each cell content to the column width. Recommended for queries that return several columns." +
                "\nsrc:<SRC_DIR>                            The directory to read the xml files from. Defaults to Current Dir." +
                "\nxpaths:<XPATH_EXP>[\\<XPATH_EXP>]         The XPAth expression (or expressions) to run over the xml files in the src dir. " +
                "\n                                         You can pass one or several back-slash-separated expressions here." +
                "\n                                         Example: //Title/Id|//Title/Name" +
                "\n                                         This will show a two-column table with Id | Name columns for each sample file on the source dir." +
                "\n");
        }

        private static void PrintLine(ref int cursorTop, string header = null, object content = null, LineSeparatorMode lineSeparatorMode = LineSeparatorMode.None)
        {
            if (lineSeparatorMode == LineSeparatorMode.BeforeContent)
            {
                if (cursorTop != -1) Console.SetCursorPosition(0, cursorTop++);
                Console.WriteLine("-".PadRight(Console.BufferWidth, '-'));
            }

            if (content != null)
            {
                if (cursorTop != -1) Console.SetCursorPosition(0, cursorTop++);
                Console.WriteLine(!string.IsNullOrEmpty(header) ? $"{header}: {content}" : $"{content}");
            }

            if (lineSeparatorMode == LineSeparatorMode.AfterContent)
            {
                if (cursorTop != -1) Console.SetCursorPosition(0, cursorTop++);
                Console.WriteLine("-".PadRight(Console.BufferWidth, '-'));
            }
        }

        private static async Task<XDocument> GetXDocument(FileSystemInfo file)
        {
            var doc = XDocument.Parse(await File.ReadAllTextAsync(file.FullName));

            // Remove xmlns  
            doc.Descendants()
                .Attributes()
                .Where(x => x.IsNamespaceDeclaration)
                .Remove();

            foreach (var elem in doc.Descendants())
                elem.Name = elem.Name.LocalName;

            foreach (var attr in doc.Descendants().Attributes())
            {
                var elem = attr.Parent;
                attr.Remove();
                elem?.Add(new XAttribute(attr.Name.LocalName, attr.Value));
            }

            return doc;
        }

        private static T ProcessFlag<T>(List<string> args, string flagPrefix, T defaultValue)
        {
            T value;
            var flagContext = args.FirstOrDefault(a => a.StartsWith(flagPrefix));
            if (!string.IsNullOrEmpty(flagContext))
            {
                var trimmedArg = flagContext?.Replace(flagPrefix, "").Trim();

                //for a bool flag, just return true if it's present, false otherwise.
                if (typeof(T) == typeof(bool))
                    trimmedArg = "true";

                value = (T)Convert.ChangeType(trimmedArg, typeof(T));

                //delete the arg from the list
                args.RemoveAll(x => x.StartsWith(flagPrefix));
            }
            else
                value = defaultValue;

            return value;
        }

        private static T[] ProcessArrayFlag<T>(List<string> args, string flagPrefix)
        {
            var result = new List<T>();
            var flagContext = args.FirstOrDefault(a => a.StartsWith(flagPrefix));
            if (!string.IsNullOrEmpty(flagContext))
            {
                var trimmedArg = flagContext?
                    .Replace(flagPrefix, "")
                    .Trim();

                //split the value by " "
                var tokens = trimmedArg?.Split("\\", StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim('"').Trim());
                result.AddRange(tokens?.Select(token => (T)Convert.ChangeType(token, typeof(T))));

                //delete the arg from the list
                args.RemoveAll(x => x.StartsWith(flagPrefix));
            }

            return result.ToArray();
        }
    }

    internal enum LineSeparatorMode
    {
        None,
        BeforeContent,
        AfterContent
    }
}