import com.solid.Gen2;
import com.solid.ReaderException;
import com.solid.SReader;

public class WriteEpc {
	static SReader reader = null;
	
	public static void main(String[] args) {
		// TODO Auto-generated method stub
		try {
			reader = SReader.create("tcp://192.168.1.136");
			//Serial port connection
			//reader = SReader.create("sld:///COM4");
			
			reader.Connect();
			
			//Enable antenna 1, power off without loss
			int[] antList = new int[] {1};
			reader.setAntenna(antList);
			
			//Set the read power to 30 without loss
			int power = 30;
			reader.setReaderPower(power);
			
			byte[] newEpc = {(byte) 0xE2, 0x11, 0x22, 0x33};
			int len = 2;
			int accessPassword = 0;
			
			Gen2.WriteData data = new Gen2.WriteData(len, newEpc, accessPassword);
			reader.WriteEPC(data);
					
			reader.ShutDown();
		} catch (ReaderException e) {
			// TODO Auto-generated catch block
			e.printStackTrace();
		}
	}

}
