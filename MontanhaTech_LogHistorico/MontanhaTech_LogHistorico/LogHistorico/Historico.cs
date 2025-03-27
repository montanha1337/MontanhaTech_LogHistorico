using System;
using System.Collections.Generic;
using System.IO;
using System.Data.SQLite;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;
using System.Security.Policy;
using System.Text;
using System.Configuration;

namespace MontanhaTech_LogHistorico
{
    public class Historico
    {
        List<HistoricoNavegacao> listaHistorico;


        public void start()
		{
			try
            {
                CaptureHistorico();

            } catch (Exception C)
			{
                GravarEmTxt(C.Message, "LogErro");
			}
        }

        public void CaptureHistorico()
        {
            listaHistorico = new List<HistoricoNavegacao>();
            ObterHistoricoChrome(100);
            ObterHistoricoEdge(100);
            ObterHistoricoRede();

            if (listaHistorico.Count.Equals(0))
            {
                GravarEmTxt("Nenhum histórico encontrado.");
                return;
            }
            FiltrarENovaGravacao(listaHistorico);
        }

        public List<HistoricoNavegacao> ObterHistoricoChrome(int quantidade)
        {
            string caminhoBanco = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google\\Chrome\\User Data\\Default\\History"
            );

            if (!File.Exists(caminhoBanco))
            {
                Console.WriteLine("O arquivo de histórico do Chrome não foi encontrado.");
                return listaHistorico;
            }

            // Criando uma cópia do banco para evitar erros de bloqueio do Chrome
            string copiaBanco = Path.Combine(Path.GetTempPath(), "ChromeHistoryCopy.db");
            File.Copy(caminhoBanco, copiaBanco, true);

            // Conexão com o banco SQLite
            using (var conn = new SQLiteConnection($"Data Source={copiaBanco};Version=3;"))
            {
                conn.Open();
                string query = $"SELECT url, title, last_visit_time FROM urls ORDER BY last_visit_time DESC LIMIT {quantidade}";

                using (var cmd = new SQLiteCommand(query, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        listaHistorico.Add(new HistoricoNavegacao
                        {
                            Navegador = "Chrome",
                            Titulo = reader["title"].ToString(),
                            Url = reader["url"].ToString(),
                            DataHora = ConverterWebKitParaDateTime(Convert.ToInt64(reader["last_visit_time"]))
                        });
                    }
                }
            }

            // Remove o arquivo temporário após a consulta
            File.Delete(copiaBanco);

            return listaHistorico;
        }

        public void ObterHistoricoEdge(int quantidade)
        {
            string caminhoBanco = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Microsoft", "Edge", "User Data", "Default", "History");

            if (!File.Exists(caminhoBanco))
            {
                GravarEmTxt("O arquivo de histórico do Edge não foi encontrado.");
            }

            // Criando uma cópia do banco para evitar bloqueios
            string copiaBanco = Path.Combine(Path.GetTempPath(), "EdgeHistoryCopy.db");
            File.Copy(caminhoBanco, copiaBanco, true);

            // Conexão com o banco SQLite
            using (var conn = new SQLiteConnection($"Data Source={copiaBanco};Version=3;"))
            {
                conn.Open();
                string query = $"SELECT url, title, last_visit_time FROM urls ORDER BY last_visit_time DESC LIMIT {quantidade}";

                using (var cmd = new SQLiteCommand(query, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        listaHistorico.Add(new HistoricoNavegacao
                        {
                            Navegador = "Edge",
                            Titulo = reader["title"].ToString(),
                            Url = reader["url"].ToString(),
                            DataHora = ConverterWebKitParaDateTime(Convert.ToInt64(reader["last_visit_time"]))
                        });
                    }
                }
            }

            // Remove o arquivo temporário
            File.Delete(copiaBanco);
        }

        // Método para capturar pacotes de rede e gravar histórico
        public void ObterHistoricoRede()
        {
            var dispositivos = CaptureDeviceList.Instance;

            // Escolhe o primeiro dispositivo disponível para captura
            foreach (var dispositivo in dispositivos)
            {
                dispositivo.Open();
                // Registra o evento de chegada de pacotes
                dispositivo.OnPacketArrival += new PacketArrivalEventHandler((sender, e) =>
                {
                    // Converte os dados do pacote em um formato legível
                    var pacote = Packet.ParsePacket(e.GetPacket().LinkLayerType, e.GetPacket().Data);

                    // Verifica se o pacote é Ethernet
                    if (pacote is EthernetPacket pacoteEthernet)
                    {
                        var url = ExtrairUrlPacote(pacoteEthernet);
                        if (!string.IsNullOrEmpty(url))
                        {
                            listaHistorico.Add(new HistoricoNavegacao
                            {
                                Navegador = dispositivo.Description, // Por enquanto, definimos como desconhecido
                                Titulo = "Título não disponível", // Pode ser ajustado se encontrarmos mais dados
                                Url = url,
                                DataHora = DateTime.Now
                            });
                        }
                    }
                });

                // Inicia a captura de pacotes
                dispositivo.StartCapture();

                // Aguarda a captura por 10 segundos
                System.Threading.Thread.Sleep(5000);

                // Para a captura
                dispositivo.StopCapture();
            }
        }

        // Método para extrair URLs dos pacotes Ethernet
        public static string ExtrairUrlPacote(EthernetPacket pacoteEthernet)
        {
            // Verificar se o pacote contém um payload (dados de requisição)
            if (pacoteEthernet.PayloadData != null && pacoteEthernet.PayloadData.Length > 0)
            {
                // Converte os dados do pacote para uma string
                string dadosPacote = Encoding.ASCII.GetString(pacoteEthernet.PayloadData);

                // Verificar se o pacote contém uma requisição HTTP
                if (dadosPacote.Contains("GET") || dadosPacote.Contains("Host:"))
                {
                    // Extrair URL do método GET
                    var linhas = dadosPacote.Split('\n');
                    foreach (var linha in linhas)
                    {
                        if (linha.Contains("GET"))
                        {
                            // Pega o caminho após o "GET"
                            var partes = linha.Split(' ');
                            if (partes.Length > 1)
                            {
                                // Retorna a URL completa (caminho relativo + domínio)
                                return "http://" + partes[1];  // Isso pode ser ajustado para incluir o host real
                            }
                        } else if (linha.Contains("Host:"))
                        {
                            // Pode pegar o host (domínio) a partir do cabeçalho HTTP "Host"
                            var partes = linha.Split(' ');
                            if (partes.Length > 1)
                            {
                                return "http://" + partes[1];  // Apenas domínio sem caminho
                            }
                        }
                    }
                }
            }

            return null;
        }

        public static DateTime ConverterWebKitParaDateTime(long webkitTimestamp)
        {
            // WebKit usa microsegundos desde 01/01/1601
            DateTime epoch = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddMilliseconds(webkitTimestamp / 1000).ToLocalTime();
        }

        public static void FiltrarENovaGravacao(List<HistoricoNavegacao> historico)
        {
            string pasta = CarregarConfiguracao();
            string arquivo = string.Format(@"LogHistorico_Temp{0}.txt", DateTime.Now.ToString("ddMMyyyy"));
            string caminhoArquivo = Path.Combine(pasta, arquivo);

            // Se o arquivo já existe, lê os registros salvos
            HashSet<string> registrosExistentes = new HashSet<string>();
            if (File.Exists(caminhoArquivo))
            {
                registrosExistentes = new HashSet<string>(File.ReadAllLines(caminhoArquivo));
            }

            // Lista apenas os novos registros (que não estão no arquivo)
            List<string> novosRegistros = new List<string>();

            foreach (var item in historico)
            {
                string registro = $"{Environment.MachineName} | {item.DataHora.ToString("dd/MM/yy HH:mm")} | {item.Navegador} | {item.Titulo} | {item.Url}";

                if (!registrosExistentes.Contains(registro))
                {
                    novosRegistros.Add(registro);
                }
            }

            // Grava apenas os novos registros no arquivo
            if (novosRegistros.Count > 0)
            {
                File.AppendAllLines(caminhoArquivo, novosRegistros);
            }
        }

        public static void GravarEmTxt(string conteudo, string Arquivo = "LogHistorico")
        {
            string pasta = CarregarConfiguracao();
            string arquivo = string.Format(@"{0}_Temp{1}.txt", Arquivo, DateTime.Now.ToString("ddMMyyyy"));
            string caminhoArquivo = Path.Combine(pasta, arquivo);

            if (!Directory.Exists(pasta))
            {
                Directory.CreateDirectory(pasta);
            }

            //File.Delete(caminhoArquivo);

            File.AppendAllText(caminhoArquivo, $"{DateTime.Now}: {conteudo}{Environment.NewLine}");
        }

        // Carregar a configuração a partir do app.config
        public static string CarregarConfiguracao()
        {
            // Recupera o caminho do arquivo de log da chave "LogFilePath" no app.config
            string caminhoLogs = ConfigurationManager.AppSettings["LogFilePath"];

            // Se o caminho não for encontrado, use um valor padrão
            if (string.IsNullOrEmpty(caminhoLogs))
            {
                caminhoLogs = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "historico"); // Caminho padrão
            }

            return caminhoLogs;
        }
    }

    public class HistoricoNavegacao
    {
        public string Navegador { get; set; }
        public string Titulo { get; set; }
        public string Url { get; set; }
        public DateTime DataHora { get; set; }
    }

}
