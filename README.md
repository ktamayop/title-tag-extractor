# title-tag-extractor
A command line tool to run XPATH queries over the files in a given directory.

##Usage: 

`title-tag-extractor [arguments] [options]`

```
Arguments:
  src                       The directory to read the xml files from.
  skip                      The number of files to skip when reading.
  take                      The max number of files to read.

Options:
  -h|--help                 Show help information
  -s|--show-filename        When set displays the file name of the processed file at the first column of the results table.
  -d|--display-empty-rows   When set displays the rows even when no results are found.
  -f|--flatten-results      When set displays the results flattened for each file. If a query has several results, they'll be displayed on a single line, separated by commas.
  -t|--truncate-long-items  When set fits each cell content to the column width. Recommended for queries that return several columns.
```

##Example:

*Windows:*
1. Open a console emulator (cmd is fine also, if you're OK with that)
2. Navigate to a directory on your machine containing one or more xml files.
   `cd path\to\your\xmls\dir`
3. Run the tool.
   `title-tag-extractor . 0 10 -s -f -t "/the/xpath/query" "/another/xpath"`
4. The tool should display the results in a nicely-formatted table.
