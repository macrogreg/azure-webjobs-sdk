﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SimpleBatch;

#if false
Single test that excercises everything. 
Provides a 1 point run 

- table
    - IDict

- Cloud-based model binder 

#endif

namespace GauntletTest
{
    // Provide a single test that's:
    // - easy to excercise on any deployment 
    // - can quickly smoketest features (especially ones that are "fragile" in a production environment)
    // - easy to run and diagnose whether sucess and failure. 
    public class Class1
    {
        // Path where the config file is. 
        const string ConfigBlobName = "GauntletConfig.txt";
        const string ConfigPath = @"sb-gauntlet-functions\" + ConfigBlobName;
        const string TempContainer = @"sb-gauntlet";
        const string TempBlobPath = TempContainer + @"\subdir\test.txt";
        const string TempBlobOutPath = TempContainer + @"\subdir\testOutput.txt";
        
        const string CookieBlobName = @"cookies\{0}.txt";

        public static void Initialize(IConfiguration config)
        {
            config.Add<Guid>(new GuidBlobBinder());

            string path = string.Format(CookieBlobName, "{cookie}");
            config.Register("FromBlob2").
                BindBlobInput("input", TempContainer + @"\" + path).
                BindBlobOutput("receipt", TempContainer + @"\cookies\{cookie}.output");
        }

        [NoAutomaticTrigger]
        public static void Start(
            ICall call,
            [Config(ConfigBlobName)] Payload val)
        {
            // Pass the guid through. 
            Guid g = Guid.NewGuid();
            Console.WriteLine("Starting a gauntlet run. Cookie: {0}", g);

            // Config gets modiifed halfway through, so may be in random state unless we rebublish. 
            // Don't care. 
            Console.WriteLine("Initial values: {0},{1}", val.Name, val.Quota);

            call.QueueCall("FromCall", new { name = val.Name, value = val.Quota, cookie = g });
        }

        [NoAutomaticTrigger]
        public static void FromCall(string name, int value, Guid cookie, [QueueOutput] out Payload gauntletQueue)
        {
            gauntletQueue = new Payload
            {
                Name = name,
                Quota = value, 
                Cookie = cookie
            };                            
        }

        public static void FromQueue(
            [QueueInput] Payload gauntletQueue,
            [BlobOutput(ConfigPath)] TextWriter twConfig,
            [BlobOutput(TempBlobPath)] TextWriter twOther
             )
        {
            // Overwrite the config file! Make sure that subsequent runs pick up the update. 
            string msg = @"{ 'Name' : 'Bob', 'Quota' : 2048 }".Replace('\'', '\"');
            twConfig.WriteLine(msg);

            twOther.WriteLine(gauntletQueue.Cookie);
        }

        public static void FromBlob(
            [BlobInput(TempBlobPath)] Guid cookie, // uses custom binder
            [BlobOutput(TempBlobOutPath)] TextWriter receipt, // output blob acts as a "receipt" of execution
            [Config(ConfigBlobName)] Payload val, 
            IBinder binder
            )
        {
            receipt.WriteLine(cookie); // proof that this function executed on latest input.

            // Config should be updated
            if ((val.Name != "Bob") || (val.Quota != 2048))
            {
                throw new Exception("Config wasn't updated");
            }

            string path = string.Format(CookieBlobName, cookie);
            TextWriter tw = binder.BindWriteStream<TextWriter>(TempContainer, path);
            tw.WriteLine(cookie);
        }

        // This function is registered via IConfig
        public static void FromBlob2(
            Stream input, // bound to BlobInput via config
            TextWriter receipt,
            Guid cookie, // bound as an event arg from the input 
            ICall call)
        {
            receipt.WriteLine(cookie); // proof that this function executed on latest input.

            // Finished the gauntlet, call final function to indicate success.
            call.QueueCall("Done", new { cookie = cookie });   
        }

        // Cookie should match the same one we passed in at start. 
        [NoAutomaticTrigger]
        public static void Done(Guid cookie)
        {
            Console.WriteLine("Success! {0}", cookie);
        }
    }

    public class Payload
    {
        public string Name { get; set; }
        public int Quota { get; set; }
        public Guid Cookie { get; set; }
    }

    // Test a custom Blob binder. 
    public class GuidBlobBinder : ICloudBlobStreamBinder<Guid>
    {
        public Guid ReadFromStream(Stream input)
        {
            string content = new StreamReader(input).ReadToEnd();
            return Guid.Parse(content);
        }

        public void WriteToStream(Guid result, Stream output)
        {
            using (var tw = new StreamWriter(output))
            {
                tw.WriteLine(result.ToString());
            }
        }
    }
}