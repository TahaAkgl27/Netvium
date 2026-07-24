using Netvium.Helpers;
using Netvium.Models;
using Netvium.Services;

static bool TestPing(string input, bool expectedValid)
{
    var ok = NetworkHostValidator.TryValidatePingTarget(input, out _, out _);
    var pass = ok == expectedValid;
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")} ping [{input}]");
    return pass;
}

static bool TestSearch(string query, ServerInfo server, string folder, bool expected)
{
    var ok = ConnectionSearchHelper.Matches(server, folder, query);
    var pass = ok == expected;
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")} search [{query}] on {server.Name}");
    return pass;
}

var allPass = true;
allPass &= TestPing("8.8.8.8", true);
allPass &= TestPing("8.8.8.8 & calc", false);
allPass &= TestSearch("", new ServerInfo("A", "10.0.0.1", "u", "p", "Prod"), "Prod", true);
allPass &= TestSearch("10.0", new ServerInfo("A", "10.0.0.1", "u", "p", "Prod"), "Prod", true);
allPass &= TestSearch("nomatch", new ServerInfo("A", "10.0.0.1", "u", "p", "Prod"), "Prod", false);

Console.WriteLine(allPass ? "ALL PASS" : "SOME FAILED");
return allPass ? 0 : 1;
