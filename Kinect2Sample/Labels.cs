using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kinect2Sample
{
    class Labels
    {
        private int label1;
        private int label2;    

        public Labels(int label1, int label2)
        {
            this.label1 = label1;
            this.label2 = label2;
        }

        public Boolean compareLabels(int label1, int label2){
            if(this.label1 == label1 && this.label2 == label2){
                return true;
            }
            else if (this.label1 == label2 && this.label2 == label1){
                return true;
            }
            else return false;
        }

        public Boolean hasEquivalentLabel(int label) {
            if (label == this.label1 || label == this.label2) {
                return true;
            }
            else return false;
        }

        public int Label2 {
            get { return label2; }
            set { label2 = value; }
        }

        public int Label1 {
            get { return label1; }
            set { label1 = value; }
        }
    }
}
