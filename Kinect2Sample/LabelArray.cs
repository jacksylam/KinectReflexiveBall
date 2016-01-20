using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kinect2Sample {
    class LabelArray {
        List<Labels> list;

        public LabelArray() {
            list = new List<Labels>();
        }

        public void Add(int label1, int label2) {
            if (list.Count == 0) {
                list.Add(new Labels(label1, label2));
            }
            else {
                Boolean addToList = true;
                for (int i = 0; i < list.Count; i++) {
                    if (list[i].compareLabels(label1, label2)) {
                        addToList = false;
                        break;
                    }
                    
                }
                if (addToList == true) {
                    list.Add(new Labels(label1, label2));
                }
            }
        }

        public int getEquivalentLabel(int label1) {
            for(int i = 0; i < list.Count; i++){
                if (list[i].hasEquivalentLabel(label1)) {
                    return list[i].Label1;
                }
            }
            return 0;
        }

        public int Size(){
            return list.Count;
        }
    }
}
