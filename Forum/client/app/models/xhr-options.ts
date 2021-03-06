import { HttpMethod } from "../definitions/http-method";

export class XhrOptions {
	url: string = "";
	timeout: number = 10000;
	method: HttpMethod = HttpMethod.Get;
	query: any = {};
	headers: { [key: string]: string } = {
		'Cache-Control': 'no-cache'
	};
	responseType: XMLHttpRequestResponseType = 'text';
	body: any = null;

	public constructor(init?: Partial<XhrOptions>) {
		Object.assign(this, init);
	}
}
