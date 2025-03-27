using System;

namespace MontanhaTech_LogHistorico
{
    public class ClasseInicial
    {
        public void InitialClass()
        {
            try
            {
                while (true)
                {
                    new Historico().start();
                }
            } catch (Exception C)
            {
                Historico.GravarEmTxt(C.Message, "LogErro");
            }
        }
    }
}
