import com.solid.Gen2;
import com.solid.Gpio_Info;
import com.solid.ReaderException;
import com.solid.ReaderListener;
import com.solid.SReader;
import com.solid.TagData;

public class ReaderGpioControl {
	static SReader reader = null;
	
	public static void main(String[] args) throws InterruptedException {
		// TODO Auto-generated method stub
		try {
			reader = SReader.create("tcp://192.168.1.136");
			
			reader.Connect();
			
			PrintListener listener = new PrintListener();
			reader.addReadListener(listener);
			
			//GPO1 outputs a high level for 3 seconds
			reader.setGPO(1, 1, 3);
			
			//Enable GPI status monitoring
			reader.Gpi_Listeners_start();
			
			Thread.sleep(10000);
			
			//Stop GPI status monitoring
			reader.Gpi_Listeners_stop();
					
			reader.ShutDown();
		} catch (InterruptedException ex) {
			
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
