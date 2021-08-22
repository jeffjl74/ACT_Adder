using System;

namespace ACT_Adder
{
    public class Player
    {
        string name_;
        string count_;
        DateTime when_;

		public string name 
		{ 
			get { return name_; }
			set { name_ = value; }
		}
		public string count
		{
			get { return count_; }
			set { count_ = value; }
		}
		public DateTime when
		{
			get { return when_; }
			set { when_ = value; }
		}
		
		public int IntCount()
        {
			int result;
			int.TryParse(count, out result);
			return result;
        }
		
        public override string ToString()
        {
            return string.Format("{0}:{1}", name, count);
        }
    }
}
