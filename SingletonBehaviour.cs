namespace alkkagi_server
{
    public class Singleton<T> where T : Singleton<T>, new()
    {
        static T instnace;
        public static T Inst
        {
            get
            {
                if (instnace == null)
                {
                    instnace = new T();
                    instnace.Init();
                }

                return instnace;
            }
        }

        protected virtual void Init()
        {

        }
    }
}
