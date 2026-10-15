import com.solid.Gen2;
import com.solid.Gpio_Info;
import com.solid.ReaderException;
import com.solid.ReaderListener;
import com.solid.SReader;
import com.solid.TagData;

public class ReadEpcFilter {
	
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
			
			//Create a callback object im that contains label callbacks, exception callbacks, and IO callbacks
			PrintListener listener = new PrintListener();
			reader.addReadListener(listener);
			
			int q = 4;
			int session = 0;
			
			int maskAdr = 32;
			byte[] maskData = {(byte) 0xE2};
			int maskLen = 8;
			
			Gen2.Select select = new Gen2.Select(Gen2.Bank.EPC, maskAdr, maskLen, maskData);
			Gen2.InventryValue value = new Gen2.InventryValue(q, session);
			for(int i = 0;i < 20;i++)
				reader.Inventry(value, select);
			
			//Gen2.EmbedTagOp embed = new Gen2.EmbedTagOp(q, session, bank, ReadAdr, ReadLen, PassWord);
			//for(int i = 0;i < 20;i++)
			//	reader.Inventry_mix(embed, select);
			
			reader.removeReadListener(listener);
			
			reader.ShutDown();
		} catch (ReaderException e) {
			// TODO Auto-generated catch block
			e.printStackTrace();
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
			if(t.getData() != null)
				System.out.println("tag epc: " + t.epcString()+" tid:"+ t.dataString() +" ant:"+ t.getAnt()+" RSSI:"+t.getRssi());
		}
	}

}
