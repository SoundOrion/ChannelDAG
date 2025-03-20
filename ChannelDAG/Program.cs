using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

class DAGNode
{
    private readonly string nodeName;
    private readonly List<string> nextNodes;
    private readonly Dictionary<string, Channel<string>> channels;
    private readonly Dictionary<string, int> dependencyCounts;
    private readonly Channel<string> dependencyChannel;

    public DAGNode(string nodeName, List<string> nextNodes, Dictionary<string, Channel<string>> channels, Dictionary<string, int> dependencyCounts = null)
    {
        this.nodeName = nodeName;
        this.nextNodes = nextNodes;
        this.channels = channels;
        this.dependencyCounts = dependencyCounts;

        if (dependencyCounts != null && dependencyCounts.ContainsKey(nodeName))
        {
            // 依存するメッセージを受信するための非同期キュー（Channel）
            dependencyChannel = Channel.CreateUnbounded<string>();
        }
    }

    public async Task StartAsync()
    {
        var channel = channels[nodeName];

        await foreach (var message in channel.Reader.ReadAllAsync())
        {
            Console.WriteLine($"[Node {nodeName}] Received: {message}");

            // ノードの処理（シミュレーション: 5秒待機）
            await Task.Delay(5000);

            if (dependencyChannel != null)
            {
                // 依存関係のあるノードの場合、キューにメッセージを追加
                await dependencyChannel.Writer.WriteAsync(message);

                // まだすべての依存メッセージを受信していない場合は待機
                if (dependencyChannel.Reader.Count < dependencyCounts[nodeName])
                {
                    Console.WriteLine($"[Node {nodeName}] Waiting for {dependencyCounts[nodeName] - dependencyChannel.Reader.Count} more dependencies.");
                    continue;
                }

                // すべての依存関係を受信したら処理を開始
                for (int i = 0; i < dependencyCounts[nodeName]; i++)
                {
                    _ = await dependencyChannel.Reader.ReadAsync();
                }

                Console.WriteLine($"[Node {nodeName}] All dependencies met, processing...");
            }

            // 次のノードへメッセージを送信
            foreach (var nextNode in nextNodes)
            {
                var nextChannel = channels[nextNode];
                var processedMessage = $"Processed by {nodeName}";

                await nextChannel.Writer.WriteAsync(processedMessage);
                Console.WriteLine($"[Node {nodeName}] Sent to {nextNode}: {processedMessage}");
            }
        }
    }
}

class Program
{
    static async Task Main()
    {
        // DAG の依存関係のカウント（D は B と C の 2 つのメッセージを待つ, E は D を待つ）
        var dependencyCounts = new Dictionary<string, int>
        {
            { "D", 2 }, // D は B と C の 2 つの処理完了を待つ
            { "E", 1 }  // E は D の処理完了を待つ
        };

        // 各ノードのメッセージを送るためのChannelを作成
        var channels = new Dictionary<string, Channel<string>>
        {
            { "A", Channel.CreateUnbounded<string>() },
            { "B", Channel.CreateUnbounded<string>() },
            { "C", Channel.CreateUnbounded<string>() },
            { "D", Channel.CreateUnbounded<string>() },
            { "E", Channel.CreateUnbounded<string>() }
        };

        // DAG ノードの作成
        var dagNodes = new Dictionary<string, DAGNode>
        {
            { "A", new DAGNode("A", new List<string> { "B", "C" }, channels) },
            { "B", new DAGNode("B", new List<string> { "D" }, channels) },
            { "C", new DAGNode("C", new List<string> { "D" }, channels) },
            { "D", new DAGNode("D", new List<string> { "E" }, channels, dependencyCounts) }, // 依存関係あり
            { "E", new DAGNode("E", new List<string>(), channels, dependencyCounts) } // 依存関係あり
        };

        // 各ノードを非同期で実行
        var tasks = new List<Task>();
        foreach (var node in dagNodes.Values)
        {
            tasks.Add(node.StartAsync());
        }

        // 初期メッセージをノードAに送信
        await channels["A"].Writer.WriteAsync("Start DAG");

        Console.WriteLine("DAG Execution Started. Press Enter to exit.");
        Console.ReadLine();
    }
}
