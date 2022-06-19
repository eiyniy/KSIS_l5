using System;

class Program
{
    static void Main(string[] args)
    {
        string url = "http://localhost:8005/";
        string filePath = "D://files//";

        HttpFilesHandler server = new HttpFilesHandler(url, filePath);
        server.Start();

        Console.WriteLine("Press any key to stop...");
        Console.ReadKey();
        server.Stop();
    }
}
