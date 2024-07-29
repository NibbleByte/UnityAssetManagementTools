| README.md |
|:---|

<div align="center">

![NaturalStringExtensions](asset/natural-string-extensions-logo.png)

</div>

<h1 align="center">NaturalStringExtensions</h1>
<div align="center">

 Micro-library for sorting strings using natural sort order i.e. Alphabetical order for humans.

[![NuGet Version](http://img.shields.io/nuget/v/NaturalStringExtensions.svg?style=flat-square)](https://www.nuget.org/packages/NaturalStringExtensions/) [![.NET 5+](https://img.shields.io/badge/.NET%20-%3E%3D%205.0-512bd4)](https://dotnet.microsoft.com/download) [![.NET Standard 2.0](https://img.shields.io/badge/.NET%20Standard-%3D%202.0-512bd4)](https://dotnet.microsoft.com/download) [![.NET Framework 4.6.1+](https://img.shields.io/badge/.NET%20Framework%20-%3E%3D%204.6.1-512bd4)](https://dotnet.microsoft.com/download) [![Stack Overflow](https://img.shields.io/badge/stack%20overflow-c%23-05930c.svg?style=flat-square)](http://stackoverflow.com/questions/tagged/c%23)

</div>

## Give a Star! :star:

If you like or are using this project please give it a star. Thanks!

## Background

A common scenario in many applications is ordering of string data using natural sort order.

Natural sort order is an ordering of strings in alphabetical order, except that multi-digit numbers are treated atomically, i.e., as if they were a single character. Natural sort order has been promoted as being more human-friendly ("natural") than the machine-oriented pure alphabetical order.

For example, in alphabetical sorting `Folder11` would be sorted before `Folder2` because `1` is sorted as smaller than `2`, while in natural sorting `Folder2` is sorted before `Folder11` because `2` is sorted as smaller than `11`.

<div align="center">

| Alphabetical   | Natural                       |     | Alphabetical  | Natural                      |
| -------------- | ----------------------------- | --- | ------------- | ---------------------------- |
| `Folder1`      | `Folder1`                     |     | `v1.2.0`      | `v1.2.0`                     |
| `Folder10` :x: | `Folder2`                     |     | `v10.1.0` :x: | `v2.0.0`                     |
| `Folder11` :x: | `Folder10` :white_check_mark: |     | `v10.5.3` :x: | `v2.1.0`                     |
| `Folder2`      | `Folder11` :white_check_mark: |     | `v2.0.0`      | `v3.1.0`                     |
| `Folder20`     | `Folder20`                    |     | `v2.1.0`      | `v10.1.0` :white_check_mark: |
| `Folder35`     | `Folder35`                    |     | `v3.1.0`      | `v10.5.3` :white_check_mark: |

</div>

Example scenarios where `NaturalStringExtensions` can be useful include sorting of file names, folder names, and version numbers.

## Getting started :rocket:

Install the [NaturalStringExtensions](https://www.nuget.org/packages/NaturalStringExtensions) package from NuGet:

```powershell
Install-Package NaturalStringExtensions
```

Use one of the `IEnumerable<T>` extension methods to sort a list based on a string field, using natural sort order:

```csharp
var folderNames = new[]
{
    "Folder20",
    "Folder1",
    "Folder2",
    "Folder10",
};

var sortedfolderNames = folderNames.OrderByNatural();

foreach (var folderName in sortedfolderNames)
{
    Console.WriteLine(folderName);
}

```

Output:

```
Folder1
Folder2
Folder10
Folder20
```

---

In the [sample](sample/) folder, there's an example of a Console application that uses `NaturalStringExtensions` to order a list of versions using natural sort order, as described above.

## Extension methods

Use one of the `IEnumerable<T>` extension methods to sort a list based on a string field, using a natural sort order:

| Extension method           | Description                                                                              |
| -------------------------- | ---------------------------------------------------------------------------------------- |
| `OrderByNatural`           | Sorts the elements of a sequence in natural ascending order                              |
| `OrderByNaturalDescending` | Sorts the elements of a sequence in natural descending order                             |
| `ThenByNatural`            | Performs a subsequent ordering of the elements in a sequence in natural ascending order  |
| `ThenByNaturalDescending`  | Performs a subsequent ordering of the elements in a sequence in natural descending order |

## NaturalStringComparer

A `NaturalStringComparer` class that implements `IComparer<string>` is available for comparing strings using a natural sort order:

```csharp
const string left = "Folder 10";
const string right = "Folder 5";

var result = new NaturalStringComparer().Compare(left, right);
// 1 -> "Folder 10" is > "Folder 5"
```

For convenience, the `NaturalStringComparer` class has a static property called `Instance` with a thread-safe instance of `NaturalStringComparer` ready for use, which is also cached upon first use.

```csharp
using System.IO;
// ...

var currentDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());

var sortedDirectoryNames = currentDirectory.EnumerateDirectories()
    .OrderBy(d => d.FullName, NaturalStringComparer.Ordinal);
```

### Sorting Arrays without LINQ

```csharp
var folderNames = new[]
{
    "Folder1000",
    "Folder200",
    "Folder30",
    "Folder4",
};

Array.Sort(folderNames, NaturalStringComparer.Ordinal);

// Contents of folderNames array:
//
// Folder4
// Folder30
// Folder200
// Folder1000
//
```

## Release History

Click on the [Releases](https://github.com/augustoproiete/NaturalStringExtensions/releases) tab on GitHub.

---

_Copyright &copy; 2021-2022 C. Augusto Proiete & Contributors - Provided under the [Apache License, Version 2.0](LICENSE). `NaturalStringExtensions` logo is a derivative of work by [Benjamin STAWARZ](https://www.iconfinder.com/bensta) ([original](https://www.iconfinder.com/icons/6138342/desc_direction_down_numeric_sort_filter_icon))._
