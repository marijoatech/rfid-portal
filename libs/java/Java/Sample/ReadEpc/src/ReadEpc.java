import java.util.HashSet;

import com.solid.Gen2;
import com.solid.Gpio_Info;
import com.solid.ReaderException;
import com.solid.ReaderListener;
import com.solid.SReader;
import com.solid.TagData;

public class ReadEpc {
	
	static SReader reader = null;
	
	public static void main(String[] args) {
		// TODO Auto-generated method stub
		try {
			//TCP connection
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
			
			//Create a callback object im that contains label callbacks, exception callbacks, and IO callbacks
			PrintListener listener = new PrintListener();
			reader.addReadListener(listener);
			
			Gen2.InventryValue value = new Gen2.InventryValue(4, 0);
			for(int i = 0;i < 20;i++)
				reader.Inventry(value, null);
			
			reader.removeReadListener(listener);
			
			reader.ShutDown();
		} catch (ReaderException e) {
			// TODO Auto-generated catch block
			//e.printStackTrace();
			System.out.println("error:"+e.getMessage());
		}
		
	}
	
	static class PrintListener implements ReaderListener
	{

		@Override
		public void ReaderGPIO(Gpio_Info gpio_info) {
			// TODO Auto-generated method stub
			System.out.println("Id:"+gpio_info.getID()+"  High:"+gpio_info.getHigh() );
		}

		@Override
		public void ReaderException(Exception e) {
			// TODO Auto-generated method stub
			System.out.println("ee ReaderException: " + e.getMessage());
		}

		@Override
		public void TagReadData(TagData t) {
			// TODO Auto-generated method stub
			System.out.println("tag epc: " + t.epcString()+" ant:"+ t.getAnt()+" RSSI:"+t.getRssi());
		}
	}

}
