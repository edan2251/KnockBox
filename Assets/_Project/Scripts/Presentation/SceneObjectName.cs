using System.Text;

namespace Knockbox.Presentation
{
    /// <summary>팀이 관리하는 표시 오브젝트 이름. 파일 확장자·GUID·게임 식별자에는 적용하지 않는다.</summary>
    public static class SceneObjectName
    {
        public static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Object";
            var result = new StringBuilder(value.Length);
            bool hasWord=false;
            foreach (char c in value)
            {
                bool letterOrDigit=IsAsciiLetterOrDigit(c);
                result.Append(letterOrDigit || c == '_' ? c : '_');
                hasWord|=letterOrDigit;
            }
            return hasWord?result.ToString():"Object";
        }

        static bool IsAsciiLetterOrDigit(char c)=>(c>='A'&&c<='Z')||(c>='a'&&c<='z')||(c>='0'&&c<='9');

        // 파일은 오브젝트 이름과 다른 정책이다. 기존 한글 폰트/원본 파일을 이름 변경 대상으로 만들지 않는다.
        public static string FileStem(string value)
        {
            if(string.IsNullOrEmpty(value))return "Object";
            var result=new StringBuilder(value.Length);
            foreach(char c in value)result.Append(char.IsLetterOrDigit(c)||c=='_'?c:'_');
            return result.ToString();
        }

        /// <summary>Transform.Find의 경로 구분자 / 는 유지하고 각 오브젝트 이름만 정리한다.</summary>
        public static string Path(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var parts = path.Split('/');
            for (int i = 0; i < parts.Length; i++)
                if (parts[i].Length > 0) parts[i] = Normalize(parts[i]);
            return string.Join("/", parts);
        }
    }
}
