using System;
using System.ComponentModel;

namespace ACT_Adder
{
    public class Player : INotifyPropertyChanged
    {
        string name_;
        string count_;
        DateTime when_;

		public string name 
		{ 
			get { return name_; }
			set { name_ = value; NotifyPropertyChanged("name"); }
		}
		public string count
		{
			get { return count_; }
			set { count_ = value; NotifyPropertyChanged("count"); }
		}
		public DateTime when
		{
			get { return when_; }
			set { when_ = value; NotifyPropertyChanged("when"); }
		}

        public event PropertyChangedEventHandler PropertyChanged;

		private void NotifyPropertyChanged(string p)
        {
			if (PropertyChanged != null)
				PropertyChanged(this, new PropertyChangedEventArgs(p));
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
