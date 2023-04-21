using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;
using McMaster.Extensions.CommandLineUtils;

namespace TitleTagExtractor
{
    [Command(Name = "title-tag-extractor", Description = "A command line utility to run xpath queries over the files in a directory.", ThrowOnUnexpectedArgument = false)]
    [HelpOption("-h|--help")]
    public class TitleTagExtractor
    {
        static Task<int> Main(string[] args) => CommandLineApplication.ExecuteAsync<TitleTagExtractor>(args);

        [Option("-s|--show-filename", Description = "When set displays the file name of the processed file at the first column of the results table.")]
        public bool ShowFileName { get; set; }

        [Option("-d|--display-empty-rows", Description = "When set displays the rows even when no results are found.")]
        public bool DisplayEmptyRows { get; set; }

        [Option("-f|--flatten-results", Description = "When set displays the results flattened for each file. If a query has several results, they'll be displayed on a single line, separated by commas.")]
        public bool FlattenResults { get; set; }

        [Option("-t|--truncate-long-items", Description = "When set fits each cell content to the column width. Recommended for queries that return several columns.")]
        public bool TruncateLongItems { get; set; }

        [Argument(0, "src", Description = "The directory to read the xml files from.")]
        [Required(ErrorMessage = "{0} argument is required. Pass . for current directory.")]
        [DirectoryExists]
        public string Src { get; set; }

        [Argument(1, "skip", Description = "The number of files to skip when reading.")]
        public int Skip { get; set; } = 0;

        [Argument(2, "take", Description = "The max number of files to read.")]
        public int Take { get; set; }

        public string[] RemainingArguments { get; } //the xpath expressions

        

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
                                colValues.Add(value.Replace("\n", "").Replace("\r", ""));
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
            if (totalCols == 0)
                return;

            var paddingData = new (int col, int length, int padding)[totalCols];
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
            //the shortest column first and leave the remaining space to the largest columns.
            paddingData = paddingData.OrderBy(x => x.length).ToArray();

            //start resolving the padding for the columns
            var availableSpace = Console.BufferWidth - totalCols + 1;
            var maxColumnPadding = availableSpace / totalCols;
            var columnsToDistribute = totalCols;

            for (var i = 0; i < paddingData.Length; i++)
            {
                var padding = Math.Min(maxColumnPadding, paddingData[i].length);
                paddingData[i].padding += padding;

                //subtract the space needed for this column of the available space and recalculate the max column padding with one column less.
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
                {
                    var row = i + rowOffset;
                    var col = j + colOffset;

                    var value = (sourceMatrix[i, j] ?? "").Replace("\n", "").Replace("\r", "");
                    if (row >= targetMatrix.GetLength(0) || col >= targetMatrix.GetLength(1))
                    {
                        Debug.WriteLine($"Some items were not displayed because the data shape is different: {value}");
                        continue;
                    }

                    targetMatrix[row, col] = value;
                }
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
    }
}
