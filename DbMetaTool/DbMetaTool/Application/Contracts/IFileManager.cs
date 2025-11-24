namespace DbMetaTool.Application.Contracts;

internal interface IFileManager
{
    void SaveFile(string path, string content);
}
