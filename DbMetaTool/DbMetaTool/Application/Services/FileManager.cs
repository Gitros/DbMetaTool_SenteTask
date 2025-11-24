using DbMetaTool.Application.Contracts;
using System.Text;

namespace DbMetaTool.Application.Services;

/// <summary>
/// Odpowiada za fizyczne zapisywanie treści tekstowych do plików.
/// Implementacja jest prosta — tworzy katalogi jeśli nie istnieją
/// i nadpisuje plik, jeśli już istnieje.
/// </summary>
internal class FileManager : IFileManager
{
    /// <summary>
    /// Zapisuje tekst do wskazanego pliku na dysku.
    /// Jeśli katalog nie istnieje – zostanie utworzony.
    /// Jeśli plik istnieje – zostanie nadpisany.
    /// </summary>
    /// <param name="path">Pełna ścieżka do pliku, który ma zostać zapisany.</param>
    /// <param name="content">Treść, która zostanie zapisana do pliku.</param>
    /// <exception cref="ArgumentException">
    /// Rzucane, gdy ścieżka do pliku jest pusta lub null.
    /// </exception>
    public void SaveFile(string path, string content)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("File path cannot be null or empty.", nameof(path));

        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content, Encoding.UTF8);
    }
}
