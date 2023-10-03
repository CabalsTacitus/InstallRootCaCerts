using System.Diagnostics.CodeAnalysis;
using CliWrap;

const string RootCaFile = "/cacert.pem";
//log will be sent to std out.
string[] OsInfoFiles = new[] {"/etc/os-release","/usr/lib/os-release" };
if (File.Exists("/etc/os-release") && !string.IsNullOrWhiteSpace(File.ReadAllText("/etc/os-release")))
{
    await InstallCertsForDistroAsync(IdentifyDistro("/etc/os-release"));
}
else if (File.Exists("/usr/lib/os-release") && !string.IsNullOrWhiteSpace(File.ReadAllText("/usr/lib/os-release")))
{
    await InstallCertsForDistroAsync(IdentifyDistro("/usr/lib/os-release"));
}
else
{
    Console.WriteLine("Could not identify distro, so did not install certs.");
}

async Task InstallCertsForDistroAsync(Distro distro)
{
    switch (distro)
    {
        case Distro.Alpine: //Append your self-signed cert to /etc/ssl/certs/ca-certificates.crt
            string certsToInstall = File.ReadAllText(RootCaFile);
            File.AppendAllText("/etc/ssl/certs/ca-certificates.crt", certsToInstall);
            break;
        case Distro.Debian:
            await InstallDebianCertsAsync(RootCaFile);
            break;
        case Distro.Fedora:
            await InstallFedoraCertsAsync(RootCaFile);
            break;
    }
}

async Task InstallFedoraCertsAsync(string rootCaFile)
{
    //Approach #1, copy and update-ca-trust
    File.Copy(rootCaFile, "/etc/pki/ca-trust/source/anchors");
    if(File.Exists("/usr/bin/update-ca-trust"))
    {
        var result = await Cli.Wrap("/usr/bin/update-ca-trust")
                .WithWorkingDirectory(Directory.GetCurrentDirectory())
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync();
    }
    else //Approach #2, run trust anchor /cacert.pem
    {
        if (!File.Exists("/usr/bin/trust"))
        {
            //Approach #3, run p11-kit extract
            if (File.Exists("/usr/bin/p11-kit"))
            {
                var result3 = await Cli.Wrap("/usr/bin/p11-kit")
                    .WithArguments(new[] { "extract", "--comment", "--format=pem-bundle", "--filter=certificates", "--overwrite", "--purpose", "server-auth", "/etc/pki/ca-trust/extracted/pem/tls-ca-bundle.pem" })
                    .WithWorkingDirectory(Directory.GetCurrentDirectory())
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteAsync();
            }
            else
            {
                Console.WriteLine("Couldn't attempt Fedora install approach 3 because p11-kit was missing.");
                Environment.Exit(2);
            }
        }
        var result = await Cli.Wrap("/usr/bin/trust")
            .WithArguments(new[] { "anchor", rootCaFile })
            .WithWorkingDirectory(Directory.GetCurrentDirectory())
            .WithValidation(CommandResultValidation.None)
            .ExecuteAsync();
    }
}

async Task InstallDebianCertsAsync(string rootCaFile)
{
    File.Copy(rootCaFile, $"/usr/local/share/ca-certificates/{rootCaFile}");
    ///usr/sbin/update-ca-certificates
    if (!File.Exists("/usr/sbin/update-ca-certificates"))
    {
        if(!File.Exists("/usr/bin/apt"))
        {
            Console.WriteLine("update-ca-certificates missing, and can't install it because apt is missing. Not installing certs.");
            Environment.Exit(1);
        }
        //install with apt install ca-certificates: assumes apt has already been configured to use new ca cert
        var result = await Cli.Wrap("/usr/bin/apt")
            .WithArguments(new[] { "update" })
            .WithWorkingDirectory(Directory.GetCurrentDirectory())
            .WithValidation(CommandResultValidation.None)
            .ExecuteAsync();
        var result2 = await Cli.Wrap("/usr/bin/apt")
            .WithArguments(new[] { "install", "-y", "ca-certificates" })
            .WithWorkingDirectory(Directory.GetCurrentDirectory())
            .ExecuteAsync();
        //TODO error handling
    }
    //execute it
    var result3 = await Cli.Wrap("/usr/sbin/update-ca-certificates")
            //.WithArguments(new[] { "install", "-y", "ca-certificates" })
            .WithWorkingDirectory(Directory.GetCurrentDirectory())
            .ExecuteAsync();
}

Distro IdentifyDistro(string releaseFile)
{
    string contents = File.ReadAllText(releaseFile).ToLower();
    return contents switch
    {
        _ when contents.Contains("debian") => Distro.Debian,
        _ when contents.Contains("alpine") => Distro.Alpine,
        _ when contents.Contains("fedora") => Distro.Fedora,
        _ => Distro.Unknown
    };
}

enum Distro
{
    Unknown,
    Alpine,
    Debian,
    Fedora
}